using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace projekt_DB {
    class Call {
        public string functionName;
        public Dictionary<string, string> arguments;
        public Call(string JSONstring){
            Dictionary<string, Dictionary<string, string>> incoming = JsonConvert.DeserializeObject<Dictionary < string, Dictionary<string, string>>>(JSONstring);
            functionName = incoming.Keys.First();
            arguments = incoming[functionName];
        }
        
        public string this[string index] {
            get => arguments[index];
        }
    }
}