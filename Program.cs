using System;

namespace projekt_DB {
    class Program {
        static string currentLine;
        static void Main(string[] args) {
            using (CallHandler handler = new CallHandler(debug : true)){
                while((currentLine = Console.ReadLine()) != null){
                    try {
                        Console.WriteLine(handler.HandleCall(new Call(currentLine)));
                    } catch (Exception e) {
                        Console.WriteLine(e.Message);
                    }
                }
            }
        }
    }
}
