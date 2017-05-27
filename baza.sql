-- Wiktor Adamski
-- nr indeksu 272220
-- Schemat bazy danych do projektu

-- Wyczyszczenie poprzedniego stanu bazy danych
DROP TABLE IF EXISTS users CASCADE;
DROP TABLE IF EXISTS event CASCADE;
DROP TABLE IF EXISTS talk CASCADE;
DROP FUNCTION IF EXISTS change_talk_status() CASCADE;
DROP TABLE IF EXISTS user_evals_talk CASCADE;
DROP TABLE IF EXISTS user_registered_for_event CASCADE;
DROP TABLE IF EXISTS user_attends_talk CASCADE;
DROP TABLE IF EXISTS user_proposes_friends CASCADE;
DROP TABLE IF EXISTS user_likes_user CASCADE;
DROP FUNCTION IF EXISTS add_friends() CASCADE;
DROP FUNCTION IF EXISTS friends(TEXT) CASCADE;
DROP FUNCTION IF EXISTS user_plan(TEXT) CASCADE;
DROP FUNCTION IF EXISTS day_plan(TIMESTAMP) CASCADE;
DROP FUNCTION IF EXISTS best_talks(TIMESTAMP, TIMESTAMP, BOOLEAN) CASCADE;
DROP FUNCTION IF EXISTS most_popular_talks(TIMESTAMP, TIMESTAMP) CASCADE;
DROP FUNCTION IF EXISTS attended_talks(TEXT) CASCADE;
DROP FUNCTION IF EXISTS registered_for_event(TEXT) CASCADE;
DROP FUNCTION IF EXISTS number_of_attendants(TEXT) CASCADE;
DROP VIEW IF EXISTS abandoned_talks CASCADE;
DROP VIEW IF EXISTS recently_added_talks CASCADE;
DROP VIEW IF EXISTS rejected_talks CASCADE;
DROP FUNCTION IF EXISTS friends_talks(TEXT, TIMESTAMP, TIMESTAMP) CASCADE;
DROP FUNCTION IF EXISTS friends_events(TEXT, TEXT) CASCADE;
DROP DOMAIN IF EXISTS user_role CASCADE;
DROP DOMAIN IF EXISTS talk_status CASCADE;

CREATE DOMAIN user_role AS TEXT
CHECK( VALUE IN ('usr', 'org'));

-- Tabela z użytkownikami
CREATE TABLE users(
    login TEXT PRIMARY KEY,
    pssw TEXT NOT NULL,
    role user_role
);

-- Tabela z wydarzeniami
CREATE TABLE event(
    event_id TEXT PRIMARY KEY,
    begin_ts TIMESTAMP NOT NULL,
    end_ts TIMESTAMP NOT NULL
);

CREATE DOMAIN talk_status AS TEXT
CHECK( VALUE IN ('accepted', 'proposed', 'rejected'));

-- Tabela z referatami zaakceptowanymi
CREATE TABLE talk(
    talk_id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    start_ts TIMESTAMP NOT NULL,
    room INT,
    speaker TEXT NOT NULL REFERENCES users(login) ON UPDATE CASCADE,
    event_id TEXT REFERENCES event(event_id) ON UPDATE CASCADE,
    register_ts TIMESTAMP NOT NULL,
    status talk_status
);

-- TODO: Zmiana triggera na taki który zamiast usuwać zmienia status referatu
CREATE FUNCTION change_talk_status() RETURNS TRIGGER AS $$
    DECLARE num BIGINT;
    BEGIN
        SELECT COUNT(*) INTO num
        FROM talk
        WHERE talk_id = NEW.talk_id;

        IF (num >= 1) THEN 
            UPDATE talk set status = 'accepted' WHERE talk_id = NEW.talk_id;
            RETURN NULL;
        END IF;
        RETURN NEW;
    END
$$ LANGUAGE plpgsql;

CREATE TRIGGER on_insert_to_talk BEFORE INSERT ON talk
FOR EACH ROW EXECUTE PROCEDURE change_talk_status();

-- Relacja oceny referatu przez użytkownika
CREATE TABLE user_evals_talk(
    user_id TEXT REFERENCES users(login) ON UPDATE CASCADE,
    talk_id TEXT REFERENCES talk(talk_id) ON UPDATE CASCADE,
    grade INT CHECK (grade >= 0 AND grade <= 10)
);

-- Relacja bycia zapisanym na wydarzenie
CREATE TABLE user_registered_for_event(
    user_id TEXT REFERENCES users(login) ON UPDATE CASCADE,
    event_id TEXT REFERENCES event(event_id) ON UPDATE CASCADE
);

-- Relacja bycia na referacie
CREATE TABLE user_attends_talk(
    user_id TEXT REFERENCES users(login) ON UPDATE CASCADE,
    talk_id TEXT REFERENCES talk(talk_id) ON UPDATE CASCADE
);

-- Propozycja zostania przyjaciółmi
CREATE TABLE user_proposes_friends(
    sender TEXT NOT NULL REFERENCES users(login) ON UPDATE CASCADE,
    receiver TEXT NOT NULL REFERENCES users(login) ON UPDATE CASCADE
);

-- Zaakceptowane przyjaźnie
CREATE TABLE user_likes_user (
    user1 TEXT NOT NULL REFERENCES users(login) ON UPDATE CASCADE,
    user2 TEXT NOT NULL REFERENCES users(login) ON UPDATE CASCADE
);

-- Lista przyjaciół danego użytkownika
CREATE FUNCTION friends(usr TEXT) RETURNS TABLE (friend TEXT) AS $$
    (SELECT user1 FROM user_likes_user WHERE user2 = usr)
    UNION
    (SELECT user2 FROM user_likes_user WHERE user1 = usr)
$$ LANGUAGE SQL STABLE;

-- Rozpatrywanie zaproszenia do znajomych
CREATE FUNCTION add_friends() RETURNS TRIGGER AS $$
    DECLARE
        flag1 BOOLEAN;
        flag2 BOOLEAN;
    BEGIN
        -- Czy są już przyjaciółmi
        SELECT (count(*) >= 1) INTO flag1
        FROM user_likes_user
        WHERE (user1 = NEW.sender AND user2 = NEW.receiver) OR
            (user1 = NEW.receiver AND user2 = NEW.sender);
        IF (flag1 = true) THEN 
            RETURN NULL;
        END IF;

        -- Czy to jest odpowiedź na zaproszenie
        SELECT(count(*) >= 1) INTO flag2
        FROM user_proposes_friends u
        WHERE u.sender = NEW.receiver AND u.receiver = NEW.sender;
        IF (flag2 = true) THEN
            DELETE FROM user_proposes_friends u
            WHERE u.sender = NEW.receiver AND u.receiver = NEW.sender;
            INSERT INTO user_likes_user VALUES (NEW.sender, NEW.receiver);
            RETURN NULL;
        END IF;
        RETURN NEW;
    END;
$$ LANGUAGE plpgsql;

-- Trigger aktywowany przy dodawaniu przyjaźni
CREATE TRIGGER on_insert_to_user_proposes_friends BEFORE INSERT ON user_proposes_friends
FOR EACH ROW EXECUTE PROCEDURE add_friends();

-- Nadchodzące referaty dla danego użytkownika
CREATE FUNCTION user_plan(usr TEXT) RETURNS TABLE (login TEXT, talk TEXT, start_timestamp TIMESTAMP, title TEXT, room INT) AS $$
    SELECT user_id, talk_id, start_ts, title, room
    FROM user_registered_for_event
        JOIN talk USING(event_id)
    WHERE user_id = usr
        AND start_ts >= now()
        AND status = 'accepted'
    ORDER BY start_ts
$$ LANGUAGE SQL STABLE;

-- Referaty w danym dniu
CREATE FUNCTION day_plan(search_date TIMESTAMP) RETURNS TABLE (talk TEXT, start_timestamp TIMESTAMP, title TEXT, room INT) AS $$
    SELECT talk_id, start_ts, title, room
    FROM talk
    WHERE start_ts::date = search_date::date
        AND status = 'accepted'
    ORDER BY room, start_ts
$$ LANGUAGE SQL STABLE;

-- Lista najlepszych referatów w danym okresie
CREATE FUNCTION best_talks(range_s TIMESTAMP, range_e TIMESTAMP, all_grades BOOLEAN) RETURNS TABLE (talk TEXT, start_timestamp TIMESTAMP, title TEXT, room INT) AS $$
    BEGIN
    IF (all_grades = true) THEN
        RETURN QUERY SELECT talk_id, start_ts, title, room
        FROM talk 
            JOIN user_evals_talk USING(talk_id)
        WHERE range_s <= start_ts
            AND start_ts <= range_e
            AND status = 'accepted'
        GROUP BY talk_id, start_ts, title, room
        ORDER BY AVG(grade);
    ELSE
        RETURN QUERY SELECT talk_id, start_ts, title, room
        FROM talk 
            JOIN user_evals_talk USING(talk_id)
            JOIN user_attends_talk USING(talk_id, user_id)
        WHERE range_s <= start_ts
            AND start_ts <= range_e
            AND status = 'accepted'
        GROUP BY talk_id, start_ts, title, room
        ORDER BY AVG(grade);
    END IF;
    END; 
$$ LANGUAGE plpgsql STABLE;

-- Lista najbardziej popularnych referatów w danym okresie
CREATE FUNCTION most_popular_talks(range_s TIMESTAMP, range_e TIMESTAMP) RETURNS TABLE (talk TEXT, start_timestamp TIMESTAMP, title TEXT, room INT) AS $$
    SELECT talk_id, start_ts, title, room
    FROM talk
        JOIN user_attends_talk USING(talk_id)
    WHERE range_s <= start_ts
        AND start_ts <= range_e
        AND status = 'accepted'
    GROUP BY talk_id, start_ts, title, room
    ORDER BY COUNT(user_id) DESC;
$$ LANGUAGE SQL STABLE;

-- Lista referatów na których był dany użytkownik
CREATE FUNCTION attended_talks(usr TEXT) RETURNS TABLE (talk TEXT, start_timestamp TIMESTAMP, title TEXT, room INT) AS $$
    SELECT talk_id, start_ts, title, room
    FROM talk
        JOIN user_attends_talk USING(talk_id)
    WHERE talk_id = usr
        AND status = 'accepted';
$$ LANGUAGE SQL STABLE;

-- Lista zarejestrowanych na dane wydarzenie
CREATE FUNCTION registered_for_event(ev TEXT) RETURNS BIGINT AS $$
    SELECT COUNT( DISTINCT user_id)
    FROM user_registered_for_event
    WHERE event_id = ev; 
$$ LANGUAGE SQL STABLE;

-- Liczba uczestników danego wykładu
CREATE FUNCTION number_of_attendants(talk TEXT) RETURNS BIGINT AS $$
    SELECT COUNT( DISTINCT user_id)
    FROM user_attends_talk
    WHERE talk_id = talk;
$$ LANGUAGE SQL STABLE;

-- Widok referatów posortowanych wg. nieobecnych uczestników
CREATE VIEW abandoned_talks AS
    SELECT talk_id, start_ts, title, room, (registered_for_event(event_id) - number_of_attendants(talk_id)) AS user_count
    FROM talk
    WHERE status = 'accepted'
    GROUP BY talk_id, start_ts, title, room
    ORDER BY user_count DESC;

-- Widok referatów posortowanych wg. daty dodania
CREATE VIEW recently_added_talks AS
    SELECT talk_id, speaker, start_ts, title, room
    FROM talk
    WHERE status <> 'rejected'
    ORDER BY register_ts DESC;

-- Widok odrzuconych referatów
CREATE VIEW rejected_talks AS
    SELECT talk_id, speaker, start_ts, title
    FROM talk
    WHERE status = 'rejected';

-- Lista referatów wygłaszanych przez przyjaciół danego użytkownika
CREATE FUNCTION friends_talks(usr TEXT, range_b TIMESTAMP, range_e TIMESTAMP) RETURNS TABLE (talk TEXT, speakerlogin TEXT, start_timestamp TIMESTAMP, title TEXT, room INT) AS $$
    SELECT talk_id, speaker, start_ts, title, room
    FROM talk
        JOIN friends(usr) ON speaker = friend
    WHERE range_b <= start_ts
        AND start_ts <= range_e
        AND status = 'accepted'
    ORDER BY start_ts;
$$ LANGUAGE SQL STABLE;

-- Lista znajomych danego użytkownika biorący udział w danym wydarzeniu
CREATE FUNCTION friends_events(usr TEXT, ev TEXT) RETURNS TABLE (login TEXT, event TEXT, friendlogin TEXT) AS $$
    SELECT usr, event_id, user_id
    FROM user_registered_for_event
        JOIN friends(usr) ON user_id = friend
    WHERE event_id = ev;
$$ LANGUAGE SQL STABLE;

