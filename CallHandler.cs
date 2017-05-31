using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Npgsql;

namespace projekt_DB
{
    class CallHandler : IDisposable {

        delegate string FunctionHandler(Call call);
        bool DEBUG {get;}

        Dictionary<string, FunctionHandler> ImplementedFunctions;

        public CallHandler(bool debug){
            DEBUG = debug;
            ImplementedFunctions = new Dictionary<string, FunctionHandler>() {
                {"organizer" , Organizer},
                {"event", Event},
                {"user", User},
                {"talk", Talk},
                {"register_user_for_event", RegisterUserForEvent},
                {"attendance", Attendance},
                {"evaluation", Evaluation},
                {"reject", Reject},
                {"proposal", Proposal},
                {"friends", Friends},
                {"user_plan", UserPlan},
                {"day_plan", DayPlan},
                {"best_talks", BestTalks},
                {"most_popular_talks", MostPopularTalks},
                {"attended_talks", AttendedTalks},
                {"abandoned_talks", AbandonedTalks},
                {"recently_added_talks", RecentlyAddedTalks},
                {"rejected_talks", RejectedTalks},
                {"proposals", Proposals},
                {"friends_talks", FriendsTalks},
                {"friends_events", FriendsEvents},
                //{"recommended_talks", RecommendedTalks}
            };
        }
        bool IsConnectionOpen = false;

        NpgsqlConnection connection;

        public string HandleCall(Call call) {
            try {
                if (IsConnectionOpen == false){
                    if (call.functionName == "open"){
                        return Open(call);
                    } else {
                        return Error;
                    }
                } else {
                    if (ImplementedFunctions.ContainsKey(call.functionName)){
                        return ImplementedFunctions[call.functionName](call);
                    } else {
                        return NotImplemented;
                    }
                }
            } catch (Exception e) {
                if (DEBUG) return GetError(e.Message);
                return Error;
            }
        }

        #region ConnectionSetup

        // (*) open <baza> <login> <password>
        // przekazuje dane umożliwiające podłączenie Twojego programu do bazy - nazwę bazy, login oraz hasło,
        // wywoływane dokładnie jeden raz, w pierwszej linii wejścia
        // zwraca status OK/ERROR w zależności od tego czy udało się nawiązać połączenie z bazą 
        string Open(Call call) {
            var connectionStringBuilder = new NpgsqlConnectionStringBuilder();
            connectionStringBuilder.Username = call["login"];
            connectionStringBuilder.Database = call["baza"];
            connectionStringBuilder.Password = call["password"];
            connectionStringBuilder.Add("Server", "localhost");
            connection = new NpgsqlConnection(connectionStringBuilder.ToString());
            connection.Open();
            IsConnectionOpen = true;

            // Sprawdzenie, czy w bazie są już nasze struktury i ewentualne wczytanie ich z pliku baza.sql
            var query = new NpgsqlCommand("select count(*) from pg_tables where tablename = 'talk'", connection);
            if ( (Int64)query.ExecuteScalar() == 0) {
                var initQuery = new NpgsqlCommand(File.ReadAllText("./baza.sql"), connection);
                initQuery.ExecuteNonQuery();
            }

            return OK;
        }

        // (*) organizer <secret> <newlogin> <newpassword> 
        // tworzy uczestnika <newlogin> z uprawnieniami organizatora i hasłem <newpassword>,
        // argument <secret> musi być równy d8578edf8458ce06fbc5bb76a58c5ca4 // zwraca status OK/ERROR 
        string Organizer(Call call) {
            if (call["secret"] != "d8578edf8458ce06fbc5bb76a58c5ca4") {
                return Error;
            }
            var query = new NpgsqlCommand("insert into users values(@log, @pwd, 'org')", connection);
            query.Parameters.AddWithValue("log", call["newlogin"]);
            query.Parameters.AddWithValue("pwd", call["newpassword"]);
            query.ExecuteNonQuery();
            return OK;
        }
        #endregion

        #region DBModification

        // (*O) event <login> <password> <eventname> <start_timestamp> <end_timestamp>
        // rejestracja wydarzenia, napis <eventname> jest unikalny
        string Event(Call call) {
            if (!AuthorizeUser(call["login"], call["password"], "org")) {
                return Error;
            }
            var query = new NpgsqlCommand("insert into event values(@name, @bts, @ets)", connection);
            query.Parameters.AddWithValue("name", call["eventname"]);
            query.Parameters.AddWithValue("bts", DateTime.Parse(call["start_timestamp"]));
            query.Parameters.AddWithValue("ets", DateTime.Parse(call["end_timestamp"]));
            query.ExecuteNonQuery();
            return OK;
        }

        // (*O) user <login> <password> <newlogin> <newpassword>
        // rejestracja nowego uczestnika <login> i <password> służą do autoryzacji wywołującego funkcję,
        // który musi posiadać uprawnienia organizatora,
        // <newlogin> <newpassword> są danymi nowego uczestnika, <newlogin> jest unikalny
        string User(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "org")){
                return Error;
            }
            var query = new NpgsqlCommand("insert into users values(@log, @pwd, 'usr')", connection);
            query.Parameters.AddWithValue("log", call["newlogin"]);
            query.Parameters.AddWithValue("pwd", call["newpassword"]);
            query.ExecuteNonQuery();
            return OK;
        }

        
        // (*O) talk <login> <password> <speakerlogin> <talk> <title> <start_timestamp> <room> <initial_evaluation> <eventname>
        // rejestracja referatu/zatwierdzenie referatu spontanicznego, <talk> jest unikalnym identyfikatorem referatu,
        // <initial_evaluation> jest oceną organizatora w skali 0-10 – jest to ocena traktowana jak każda inna,
        // <eventname> jest nazwą wydarzenia, którego częścią jest dany referat - może być pustym napisem,
        // co oznacza, że referat nie jest przydzielony do jakiegokolwiek wydarzenia
        string Talk(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "org")){
                return Error;
            }
            using(var transaction = connection.BeginTransaction()){
                var query = new NpgsqlCommand($"insert into talk values(@talk_id, @title, @start_ts, @room, @speaker, {(call["eventname"] == "" ? "NULL" : "@eventname")}, now(), 'accepted')", connection );
                query.Parameters.AddWithValue("talk_id", call["talk"]);
                query.Parameters.AddWithValue("title", call["title"]);
                query.Parameters.AddWithValue("start_ts", DateTime.Parse(call["start_timestamp"]));
                query.Parameters.AddWithValue("room", int.Parse(call["room"]));
                query.Parameters.AddWithValue("speaker", call["speakerlogin"]);
                query.Parameters.AddWithValue("eventname", call["eventname"]);
                query.ExecuteNonQuery();

                query = new NpgsqlCommand("insert into user_evals_talk values(@usr, @talk, @val)", connection);
                query.Parameters.AddWithValue("usr", call["login"]);
                query.Parameters.AddWithValue("talk", call["talk"]);
                query.Parameters.AddWithValue("val", int.Parse(call["initial_evaluation"]));
                query.ExecuteNonQuery();
                transaction.Commit();
            }
            return OK;
        }

        // (*U) register_user_for_event <login> <password> <eventname>
        // rejestracja uczestnika <login> na wydarzenie <eventname>
        string RegisterUserForEvent(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "usr")){
                return Error;
            }
            var query = new NpgsqlCommand("insert into user_registered_for_event values(@login, @event)", connection);
            query.Parameters.AddWithValue("login", call["login"]);
            query.Parameters.AddWithValue("event", call["eventname"]);
            query.ExecuteNonQuery();
            return OK;
        }

        // (*U) attendance <login> <password> <talk>
        // odnotowanie faktycznej obecności uczestnika <login> na referacie <talk>
        string Attendance(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "usr")){
                return Error;
            }
            var query = new NpgsqlCommand("insert into user_attends_talk values(@login, @talk)", connection);
            query.Parameters.AddWithValue("login", call["login"]);
            query.Parameters.AddWithValue("talk", call["talk"]);
            query.ExecuteNonQuery();
            return OK;
        }

        // (*U) evaluation <login> <password> <talk> <rating>
        // ocena referatu <talk> w skali 0-10 przez uczestnika <login>
        string Evaluation(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "usr")){
                return Error;
            }
            var query = new NpgsqlCommand("insert into user_evals_talk values(@login, @talk, @value)", connection);
            query.Parameters.AddWithValue("login", call["login"]);
            query.Parameters.AddWithValue("talk", call["talk"]);
            query.Parameters.AddWithValue("value", int.Parse(call["rating"]));
            query.ExecuteNonQuery();
            return OK;
        }

        // (O) reject <login> <password> <talk> 
        // usuwa referat spontaniczny <talk> z listy zaproponowanych
        string Reject(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "org")){
                return Error;
            }
            var query = new NpgsqlCommand("udpate talk set status = 'rejected' where talk_id = @talk and status = 'proposed'", connection);
            query.Parameters.AddWithValue("talk", call["call"]);
            query.ExecuteNonQuery();
            return OK;
        }

        // (U) proposal  <login> <password> <talk> <title> <start_timestamp>
        // propozycja referatu spontanicznego, <talk> - unikalny identyfikator referatu
        string Proposal(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "usr")){
                return Error;
            }
            var query = new NpgsqlCommand("insert into talk values(@talk, @title, @time, NULL, @usr, NULL, now(), 'proposed')", connection);
            query.Parameters.AddWithValue("talk", call["talk"]);
            query.Parameters.AddWithValue("title", call["title"]);
            query.Parameters.AddWithValue("time", DateTime.Parse(call["start_timestamp"]));
            query.Parameters.AddWithValue("usr", call["login"]);
            query.ExecuteNonQuery();
            return OK;
        }

        // (U) friends <login1> <password> <login2>
        // uczestnik <login1> chce nawiązać znajomość z uczestnikiem <login2>,
        // znajomość uznajemy za nawiązaną jeśli obaj uczestnicy chcą ją nawiązać
        // tj. po wywołaniach friends <login1> <password1> <login2> i friends <login2> <password2> <login1>
        string Friends(Call call) {
            if (! AuthorizeUser(call["login1"], call["password"], "usr")){
                return Error;
            }
            var query = new NpgsqlCommand("insert into user_proposes_friends values(@usr1, @usr2)", connection);
            query.Parameters.AddWithValue("usr1", call["login1"]);
            query.Parameters.AddWithValue("usr2", call["login2"]);
            query.ExecuteNonQuery();
            return OK;
        }
        #endregion

        #region OtherOperations

        // (*N) user_plan <login> <limit>
        // zwraca plan najbliższych referatów z wydarzeń, na które dany uczestnik jest zapisany
        // (wg rejestracji na wydarzenia) posortowany wg czasu rozpoczęcia,
        // wypisuje pierwsze <limit> referatów, przy czym 0 oznacza, że należy wypisać wszystkie
        // Atrybuty zwracanych krotek: <login> <talk> <start_timestamp> <title> <room>
        string UserPlan(Call call) {
            int limitVal = int.Parse(call["limit"]);
            var query = new NpgsqlCommand($"select * from user_plan(@log) {(limitVal > 0 ? "LIMIT @lim" : "")}", connection);
            query.Parameters.AddWithValue("log", call["login"]);
            query.Parameters.AddWithValue("lim", limitVal);
            var results = new List<Object>();
            using (var reader = query.ExecuteReader()){
                while(reader.Read()){
                    results.Add(new {
                        login = reader.GetString(0),
                        talk = reader.GetString(1),
                        start_timestamp = reader.GetTimeStamp(2).ToString(),
                        title = reader.GetString(3),
                        room = reader.GetInt32(4).ToString()
                    });
                }
            }
            return GetResults(results);
        }

        // (*N) day_plan <timestamp> 
        // zwraca listę wszystkich referatów zaplanowanych na dany dzień
        // posortowaną rosnąco wg sal, w drugiej kolejności wg czasu rozpoczęcia
        // Atrybuty zwracanych krotek: <talk> <start_timestamp> <title> <room>
        string DayPlan(Call call) {
            var query = new NpgsqlCommand("select * from day_plan(@ts)", connection);
            query.Parameters.AddWithValue("ts", DateTime.Parse(call["timestamp"]));
            var results = new List<Object>();
            using (var reader = query.ExecuteReader()){
                while(reader.Read()){
                    results.Add(new {
                        talk = reader.GetString(0),
                        start_timestamp = reader.GetTimeStamp(1).ToString(),
                        title = reader.GetString(2),
                        room = reader.GetInt32(3).ToString()
                    });
                }
            }
            return GetResults(results);
        }

        // (*N) best_talks <start_timestamp> <end_timestamp> <limit> <all> 
        // zwraca referaty rozpoczynające się w  danym przedziale czasowym 
        // posortowane malejąco wg średniej oceny uczestników, przy czym jeśli <all>
        // jest równe 1 należy wziąć pod uwagę wszystkie oceny,
        // w przeciwnym przypadku tylko oceny uczestników, którzy byli na referacie obecni,
        // wypisuje pierwsze <limit> referatów, przy czym 0 oznacza, że należy wypisać wszystkie
        // Atrybuty zwracanych krotek: <talk> <start_timestamp> <title> <room>
        string BestTalks(Call call) {
            bool all = (call["all"] == "0");
            int limit = int.Parse(call["limit"]);
            var query = new NpgsqlCommand($"select * from best_talks(@sts, @ets, @all) {(limit > 0 ? "LIMIT @lim" : "")}",connection);
            query.Parameters.AddWithValue("sts", DateTime.Parse(call["start_timestamp"]));
            query.Parameters.AddWithValue("ets", DateTime.Parse(call["end_timestamp"]));
            query.Parameters.AddWithValue("all", all);
            query.Parameters.AddWithValue("lim", limit);
            var results = new List<Object>();
            using (var reader = query.ExecuteReader()){
                while(reader.Read()){
                    results.Add(new {
                        talk = reader.GetString(0),
                        start_timestamp = reader.GetTimeStamp(1).ToString(),
                        title = reader.GetString(2),
                        room = reader.GetInt32(3).ToString()
                    });
                }
            }
            return GetResults(results);
        }


        // (*N) most_popular_talks <start_timestamp> <end_timestamp> <limit>
        // zwraca referaty rozpoczynające się w podanym przedziału czasowego posortowane
        // malejąco wg obecności, wypisuje pierwsze <limit> referatów,
        // przy czym 0 oznacza, że należy wypisać wszystkie
        // Atrybuty zwracanych krotek: <talk> <start_timestamp> <title> <room>
        string MostPopularTalks(Call call) {
            int limit = int.Parse(call["limit"]);
            var query = new NpgsqlCommand($"select * from most_popular_talks(@sts, @ets) {(limit > 0 ? "LIMIT @lim" : "")}",connection);
            query.Parameters.AddWithValue("sts", DateTime.Parse(call["start_timestamp"]));
            query.Parameters.AddWithValue("ets", DateTime.Parse(call["end_timestamp"]));
            query.Parameters.AddWithValue("lim", limit);
            var results = new List<Object>();
            using (var reader = query.ExecuteReader()){
                while(reader.Read()){
                    results.Add(new {
                        talk = reader.GetString(0),
                        start_timestamp = reader.GetTimeStamp(1).ToString(),
                        title = reader.GetString(2),
                        room = reader.GetInt32(3).ToString()
                    });
                }
            }
            return GetResults(results);
        }

        // (*U) attended_talks <login> <password> 
        // zwraca dla danego uczestnika referaty, na których był obecny 
        // Atrybuty zwracanych krotek: <talk> <start_timestamp> <title> <room>
        string AttendedTalks(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "usr")){
                return Error;
            }
            var query = new NpgsqlCommand("select * from attended_talks(@login)",connection);
            query.Parameters.AddWithValue("login", call["login"]);
            var results = new List<Object>();
            using (var reader = query.ExecuteReader()){
                while(reader.Read()){
                    results.Add(new {
                        talk = reader.GetString(0),
                        start_timestamp = reader.GetTimeStamp(1).ToString(),
                        title = reader.GetString(2),
                        room = reader.GetInt32(3).ToString()
                    });
                }
            }
            return GetResults(results);
        }

        // (*O) abandoned_talks <login> <password>  <limit>
        // zwraca listę referatów posortowaną malejąco wg liczby uczestników <number>
        // zarejestrowanych na wydarzenie obejmujące referat, którzy nie byli na tym
        // referacie obecni, wypisuje pierwsze <limit> referatów, przy czym 0 oznacza,
        // że należy wypisać wszystkie
        // Atrybuty zwracanych krotek: <talk> <start_timestamp> <title> <room> <number>
        string AbandonedTalks(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "org")) {
                return Error;
            }
            int limit = int.Parse(call["limit"]);
            var query = new NpgsqlCommand($"select * from abandoned_talks {(limit > 0 ? "LIMIT @lim" : "")}", connection);
            query.Parameters.AddWithValue("lim", limit);
            var results = new List<Object>();
            using (var reader = query.ExecuteReader()){
                while(reader.Read()){
                    results.Add(new {
                        talk = reader.GetString(0),
                        start_timestamp = reader.GetTimeStamp(1).ToString(),
                        title = reader.GetString(2),
                        room = reader.GetInt32(3).ToString(),
                        number = reader.GetInt32(4)
                    });
                }
            }
            return GetResults(results);
        }

        // (N) recently_added_talks <limit> 
        // zwraca listę ostatnio zarejestrowanych referatów, wypisuje ostatnie <limit>
        // referatów wg daty zarejestrowania, przy czym 0 oznacza, że należy wypisać wszystkie
        // Atrybuty zwracanych krotek: <talk> <speakerlogin> <start_timestamp> <title> <room>
        string RecentlyAddedTalks(Call call) {
            int limit = int.Parse(call["limit"]);
            var query = new NpgsqlCommand($"select * from recently_added_talks {(limit > 0 ? "LIMIT @lim" : "")}", connection);
            if (limit > 0) query.Parameters.AddWithValue("lim", limit);
            var results = new List<Object>();
            using (var reader = query.ExecuteReader()){
                while(reader.Read()){
                    results.Add(new {
                        talk = reader.GetString(0),
                        start_timestamp = reader.GetTimeStamp(1).ToString(),
                        title = reader.GetString(2),
                        room = reader.GetInt32(3).ToString(),
                        number = reader.GetInt32(4)
                    });
                }
            }
            return GetResults(results);
        }

        // (U/O) rejected_talks <login> <password> 
        // jeśli wywołujący ma uprawnienia organizatora zwraca listę wszystkich odrzuconych
        // referatów spontanicznych, w przeciwnym przypadku listę odrzuconych referatów wywołującego ją uczestnika 
        // Atrybuty zwracanych krotek: <talk> <speakerlogin> <start_timestamp> <title>
        string RejectedTalks(Call call) {
            bool isOrganizer = AuthorizeUser(call["login"], call["password"], "org");
            bool isUser = AuthorizeUser(call["login"], call["password"], "usr");
            if ((!isOrganizer) && (!isUser)) return Error;
            string userSelect = isUser ? " where speaker = @usr" : "";

            var query = new NpgsqlCommand("select * from rejected_talks" + userSelect, connection);
            query.Parameters.AddWithValue("usr", call["login"]);
            var results = new List<Object>();
            using(var reader = query.ExecuteReader()) {
                while(reader.Read()) {
                    results.Add(new {
                        talk = reader.GetString(0),
                        speakerlogin = reader.GetString(1),
                        start_timestamp = reader.GetTimeStamp(2).ToString(),
                        title = reader.GetString(3)
                    });
                }
            }
            return GetResults(results);
        }

        // (O) proposals <login> <password>
        // zwraca listę propozycji referatów spontanicznych do zatwierdzenia lub odrzucenia,
        // zatwierdzenie lub odrzucenie referatu polega na wywołaniu przez organizatora
        // funkcji talk lub reject z odpowiednimi parametrami
        // Atrybuty zwracanych krotek: <talk> <speakerlogin> <start_timestamp> <title>
        string Proposals(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "org")){
                return Error;
            }
            var query = new NpgsqlCommand("select talk_id, speaker, start_ts, title from talk where status = 'proposed'", connection);
            var results = new List<Object>();
            using (var reader = query.ExecuteReader()){
                while(reader.Read()){
                    results.Add(new {
                        talk = reader.GetString(0),
                        speakerlogin = reader.GetString(1),
                        start_timestamp = reader.GetTimeStamp(2).ToString(),
                        title = reader.GetString(3),
                    });
                }
            }
            return GetResults(results);
        }

        // (U) friends_talks <login> <password> <start_timestamp> <end_timestamp> <limit>
        // lista referatów  rozpoczynających się w podanym przedziale czasowym wygłaszanych
        // przez znajomych danego uczestnika posortowana wg czasu rozpoczęcia,
        // wypisuje pierwsze <limit> referatów, przy czym 0 oznacza, że należy wypisać wszystkie
        // Atrybuty zwracanych krotek: <talk> <speakerlogin> <start_timestamp> <title> <room>
        string FriendsTalks(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "usr")){
                return Error;
            }
            int limit = int.Parse(call["limit"]);
            var query = new NpgsqlCommand($"select * from friends_talks(@usr, @bts, @ets) {(limit > 0 ? "LIMIT @lim" : "")}", connection);
            query.Parameters.AddWithValue("usr", call["login"]);
            query.Parameters.AddWithValue("bts", DateTime.Parse(call["start_timestamp"]));
            query.Parameters.AddWithValue("ets", DateTime.Parse(call["end_timestamp"]));
            if (limit > 0) query.Parameters.AddWithValue("lim", limit);
            var results = new List<Object>();
            using (var reader = query.ExecuteReader()){
                while(reader.Read()){
                    results.Add(new {
                        talk = reader.GetString(0),
                        start_timestamp = reader.GetTimeStamp(1).ToString(),
                        title = reader.GetString(2),
                        room = reader.GetInt32(3).ToString(),
                        number = reader.GetInt32(4)
                    });
                }
            }
            return GetResults(results);
        }

        // Słowem komentarza na temat typu wyniku:
        // Wychodzi na to, że słowo "event" nie może być nazwą pola w klasie anonimowej,
        // dlatego w tej metodzie używam słownika string => string

        // (U) friends_events <login> <password> <event>
        // lista znajomych uczestniczących w danym wydarzeniu
        // Atrybuty zwracanych krotek: <login> <event> <friendlogin> 
        string FriendsEvents(Call call) {
            if (! AuthorizeUser(call["login"], call["password"], "usr")){
                return Error;
            }
            int limit = int.Parse(call["limit"]);
            var query = new NpgsqlCommand($"select * from friends_events(@usr, @event) {(limit > 0 ? "LIMIT @lim" : "")}", connection);
            query.Parameters.AddWithValue("usr", call["login"]);
            query.Parameters.AddWithValue("event", call["event"]);
            if (limit > 0) query.Parameters.AddWithValue("lim", limit);
            var results = new List<Dictionary<string, string>>();
            using (var reader = query.ExecuteReader()){
                while(reader.Read()){
                    results.Add(new Dictionary<string,string>{
                        {"login", reader.GetString(0)},
                        {"event", reader.GetString(1)},
                        {"friendlogin", reader.GetString(2)},
                    });
                }
            }
            return GetResults(results);
        }
        
        //----------------------------
        // Funkcja niezaimplementowana
        //----------------------------
        // (U) recommended_talks <login> <password> <start_timestamp> <end_timestamp> <limit> 
        // zwraca referaty rozpoczynające się w podanym przedziale czasowym,
        // które mogą zainteresować danego uczestnika (zaproponuj parametr <score>
        // obliczany na podstawie dostępnych danych – ocen, obecności, znajomości itp.),
        // wypisuje pierwsze <limit> referatów wg nalepszego <score>,
        // przy czym 0 oznacza, że należy wypisać wszystkie
        // Atrybuty zwracanych krotek: <talk> <speakerlogin> <start_timestamp> <title> <room> <score>
        string RecommendedTalks(Call call) {
            throw new NotImplementedException();
        }
        #endregion

        // Sprawdza czy podany użytkownik ma wystarczające uprawnienia
        // Rzuca wyjątkami jeśli baza nie zawiera podanego użytkownika
        // (łapane przez HandleCall)
        bool AuthorizeUser(string usr, string pwd, string role){
            var query = new NpgsqlCommand("select role from users where login = @login and pssw = @pssw", connection);
            query.Parameters.AddWithValue("login", usr);
            query.Parameters.AddWithValue("pssw", pwd);
            using (var reader = query.ExecuteReader()){
                while(reader.Read()) return reader.GetString(0) == role;
            }
            return false;
        }

        string Error => "{\"status\"=\"ERROR\"}";

        string OK => "{\"status\"=\"OK\"}";

        string NotImplemented => "{\"status\"=\"NOT IMPLEMENTED\"}";

        string GetResults(List<object> results) => JsonConvert.SerializeObject(new {status = "OK", data = results});
        string GetResults(List<Dictionary<string, string>> results) => JsonConvert.SerializeObject(new {status = "OK", data = results});
        string GetError(string msg) => $"{{\"status\"=\"ERROR\", \"MSG\"=\"{msg}\"}}";

        #region IDisposable
        
        bool disposedValue = false;
        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    if (IsConnectionOpen) connection.Close();
                    connection.Dispose();
                }
                disposedValue = true;
            }
        }
        void IDisposable.Dispose() {
            Dispose(true);
        }
        #endregion
    }
    class CheckedAttribute : Attribute {}
}