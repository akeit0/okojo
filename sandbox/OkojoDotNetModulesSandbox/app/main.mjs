import "nuget:Newtonsoft.Json@13.0.3";
import process from "node:process";
console.log(`Hello from ${process.version}!`);
const Newtonsoft = clr.Newtonsoft;
const JsonConvert = Newtonsoft.Json.JsonConvert;
const JToken = Newtonsoft.Json.Linq.JToken;
const TokenType = Newtonsoft.Json.Linq.JTokenType;
const payload = JToken.Parse(`{
    "demo":"node-nuget-import",
    "engine":"okojo",
    "items":[1,2,3]
    }`);
for (const item of payload) {
    console.log(item.ToString());
}
