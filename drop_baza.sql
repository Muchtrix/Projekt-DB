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