-- Wiktor Adamski
-- nr indeksu 272220
-- Schemat bazy danych do projektu

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
            ev event;
    BEGIN
        IF EXISTS (select * from event WHERE event_id = NEW.event_id) THEN
            SELECT * FROM event WHERE event_id = NEW.event_id INTO ev;
            IF NEW.start_ts BETWEEN ev.begin_ts AND ev.end_ts THEN RETURN NEW;
            ELSE
                RETURN NULL;
            END IF;
        END IF ;
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
        RETURN QUERY SELECT t.talk_id, t.start_ts, t.title, t.room
        FROM talk t
            JOIN user_evals_talk USING(talk_id)
        WHERE range_s <= t.start_ts
            AND t.start_ts <= range_e
            AND t.status = 'accepted'
        GROUP BY t.talk_id, t.start_ts, t.title, t.room
        ORDER BY AVG(grade) DESC;
    ELSE
        RETURN QUERY SELECT t.talk_id, t.start_ts, t.title, t.room
        FROM talk t
            JOIN (
                SELECT uet.talk_id, uet.grade FROM user_evals_talk uet
                WHERE uet.user_id IN (
                    SELECT user_id FROM user_attends_talk
                    WHERE uet.talk_id = talk_id
                ) OR uet.user_id IN (
                    SELECT login FROM USERS WHERE role = 'org'
                )
            ) AS foo USING(talk_id)
        WHERE range_s <= t.start_ts
            AND t.start_ts <= range_e
            AND t.status = 'accepted'
        GROUP BY t.talk_id, t.start_ts, t.title, t.room
        ORDER BY AVG(grade) DESC;
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
    ORDER BY COUNT(DISTINCT user_id) DESC;
$$ LANGUAGE SQL STABLE;

-- Lista referatów na których był dany użytkownik
CREATE FUNCTION attended_talks(usr TEXT) RETURNS TABLE (talk TEXT, start_timestamp TIMESTAMP, title TEXT, room INT) AS $$
    SELECT talk_id, start_ts, title, room
    FROM talk
        JOIN user_attends_talk USING(talk_id)
    WHERE user_id = usr
        AND status = 'accepted';
$$ LANGUAGE SQL STABLE;

-- Lista zarejestrowanych na dane wydarzenie
CREATE FUNCTION registered_for_event(ev TEXT) RETURNS BIGINT AS $$
    SELECT COUNT( DISTINCT user_id)
    FROM user_registered_for_event
    WHERE event_id = ev; 
$$ LANGUAGE SQL STABLE;

-- Liczba osób nieobecnych na danym wykładzie
CREATE FUNCTION number_of_attendants(ev TEXT, talk TEXT) RETURNS BIGINT AS $$
    SELECT COUNT( DISTINCT user_id)
    FROM (
        (SELECT user_id from user_registered_for_event where event_id = ev)
        EXCEPT 
        (SELECT user_id from user_attends_talk where talk_id = talk)
    ) AS foo;
$$ LANGUAGE SQL STABLE;

-- Widok referatów posortowanych wg. nieobecnych uczestników
CREATE VIEW abandoned_talks AS
    SELECT talk_id, start_ts, title, room, (number_of_attendants(event_id, talk_id)) AS user_count
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

