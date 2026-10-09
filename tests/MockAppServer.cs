using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

internal static class MockAppServer
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--sentinel") { Thread.Sleep(120000); return 0; }
        var json = new JavaScriptSerializer();
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            var message = (Dictionary<string, object>)json.DeserializeObject(line);
            if (!message.ContainsKey("id")) continue;
            string method = (string)message["method"];
            string mode = File.ReadAllText(Environment.GetEnvironmentVariable("CQP_TEST_CONTROL")).Trim();
            object result = new Dictionary<string, object>();
            if (method == "account/read") result = mode == "login" ? new { account = (object)null } : (object)new { account = new { type = "chatgpt", email = "synthetic@example.invalid", planType = "plus" } };
            if (method == "account/rateLimits/read")
            {
                if (mode == "timeout") continue;
                if (mode == "http-error")
                {
                    Console.WriteLine(json.Serialize(new { id = message["id"], error = new { code = 503, message = "Synthetic service unavailable" } }));
                    continue;
                }
                var quota = new { codex = new { primary = new { usedPercent = 18, windowDurationMins = 300, resetsAt = 2000000000 }, secondary = new { usedPercent = 37, windowDurationMins = 10080, resetsAt = 2000500000 } } };
                var payload = new Dictionary<string, object> { { "rateLimitsByLimitId", quota } };
                if (mode != "legacy")
                    payload["rateLimitResetCredits"] = new {
                        availableCount = mode == "zero-credits" ? 0 : 2,
                        credits = mode == "count-only" ? null : mode == "zero-credits" ? new object[0] : new object[] {
                            new { id = "synthetic-later", status = "available", expiresAt = 2000700000 },
                            new { id = "synthetic-earlier", status = "available", expiresAt = 2000600000 }
                        }
                    };
                result = payload;
            }
            Console.WriteLine(json.Serialize(new { id = message["id"], result = result }));
        }
        return 0;
    }
}
