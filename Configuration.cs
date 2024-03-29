﻿using System.Text.Json;
using System.Text.Json.Serialization;

namespace wangwenx190.ProxyTool
{
    public class Configuration
    {
        public UInt16? proxy_port { get; set; }
        public string target_url { get; set; }
        public string ssl_decrypt_policy { get; set; }
        public string ssl_error_policy { get; set; }
        public string[] redirect_domains { get; set; }
        public Dictionary<string, string>? url_patches { get; set; }

        public static Configuration? TryParse(string fileName)
        {
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (!File.Exists(jsonPath))
            {
                Console.WriteLine(jsonPath + " doesn't exist.");
                return null;
            }
            string jsonString = File.ReadAllText(jsonPath);
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                Console.WriteLine(jsonPath + " is empty.");
                return null;
            }
            JsonSerializerOptions options = new()
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
            };
            return JsonSerializer.Deserialize<Configuration>(jsonString, options);
        }
    }
}
