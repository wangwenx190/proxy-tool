using System.Net;
using System.Net.Security;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace wangwenx190.ProxyTool
{
    internal class ProxyService
    {
        public enum SSL_Decrypt_Policy
        {
            Never,
            Always,
            OnDemand
        }

        public enum SSL_Error_Policy
        {
            AlwaysIgnore,
            NeverIgnore
        }

        public static readonly string DEFAULT_PROXY_ADDRESS = "127.0.0.1";
        public static readonly int DEFAULT_PROXY_PORT = 8080;

        private readonly ProxyServer _server;

        private readonly Uri _proxy_uri;
        private readonly Uri _target_uri;
        private readonly string[] _redirect_domains;
        private readonly Dictionary<string, string>? _url_patches;
        private readonly SSL_Decrypt_Policy _ssl_decrypt_policy;
        private readonly SSL_Error_Policy _ssl_error_policy;

        private static string? ExtractIP(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }
            string result = address;
            if (result.StartsWith("http://"))
            {
                result = result.Remove(0, 7);
            }
            else if (result.StartsWith("https://"))
            {
                result = result.Remove(0, 8);
            }
            int colonIndex = result.IndexOf(':');
            if (colonIndex >= 0)
            {
                result = result.Remove(colonIndex);
            }
            if (result.EndsWith('/'))
            {
                result = result.Remove(result.Length - 1);
            }
            return result;
        }

        private static int? ExtractPort(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }
            string portString = address;
            if (portString.StartsWith("http://"))
            {
                portString = portString.Remove(0, 7);
            }
            else if (portString.StartsWith("https://"))
            {
                portString = portString.Remove(0, 8);
            }
            if (portString.EndsWith('/'))
            {
                portString = portString.Remove(portString.Length - 1);
            }
            int colonIndex = portString.IndexOf(':');
            if (colonIndex < 0)
            {
                return null;
            }
            portString = portString.Substring(colonIndex + 1);
            return int.Parse(portString);
        }

        private static string FixAddress(string address, int? port)
        {
            string result = address;
            if (!(result.StartsWith("http://") || result.StartsWith("https://")))
            {
                result = "http://" + result;
            }
            if (port != null)
            {
                int colonIndex = result.LastIndexOf(':');
                if (colonIndex <= 5)
                {
                    if (result.EndsWith('/'))
                    {
                        result = result.Remove(result.Length - 1);
                    }
                    result += ':' + port.ToString() + '/';
                }
                else
                {
                    Console.WriteLine("Can't add port to " + address + ": it has a port number already.");
                }
            }
            if (!result.EndsWith('/'))
            {
                result += '/';
            }
            return result;
        }

        public ProxyService(Configuration config)
        {
            string? proxy_address = config.proxy_url;
            if (string.IsNullOrWhiteSpace(proxy_address))
            {
                proxy_address = FixAddress(DEFAULT_PROXY_ADDRESS, DEFAULT_PROXY_PORT);
            }
            else
            {
                int? port = ExtractPort(proxy_address);
                if (port == null)
                {
                    proxy_address = FixAddress(proxy_address, DEFAULT_PROXY_PORT);
                }
                else
                {
                    proxy_address = FixAddress(proxy_address, null);
                }
            }
            _proxy_uri = new(proxy_address);
            _target_uri = new(FixAddress(config.target_url, null));
            _redirect_domains = config.redirect_domains;
            if (config.ssl_decrypt_policy.ToLowerInvariant() == "never")
            {
                _ssl_decrypt_policy = SSL_Decrypt_Policy.Never;
            }
            else if (config.ssl_decrypt_policy.ToLowerInvariant() == "always")
            {
                _ssl_decrypt_policy = SSL_Decrypt_Policy.Always;
            }
            else if (config.ssl_decrypt_policy.ToLowerInvariant() == "ondemand")
            {
                _ssl_decrypt_policy = SSL_Decrypt_Policy.OnDemand;
            }
            else
            {
                _ssl_decrypt_policy = SSL_Decrypt_Policy.Always;
            }
            if (config.ssl_error_policy.ToLowerInvariant() == "alwaysignore")
            {
                _ssl_error_policy = SSL_Error_Policy.AlwaysIgnore;
            }
            else if (config.ssl_error_policy.ToLowerInvariant() == "neverignore")
            {
                _ssl_error_policy = SSL_Error_Policy.NeverIgnore;
            }
            else
            {
                _ssl_error_policy = SSL_Error_Policy.AlwaysIgnore;
            }
            
            _server = new();
            _server.CertificateManager.EnsureRootCertificate();

            _server.BeforeRequest += BeforeRequest;
            _server.ServerCertificateValidationCallback += OnCertValidation;

            ExplicitProxyEndPoint endPoint = new(IPAddress.Parse(ExtractIP(proxy_address)), _proxy_uri.Port, true);
            endPoint.BeforeTunnelConnectRequest += BeforeTunnelConnectRequest;

            _server.AddEndPoint(endPoint);
            _server.Start();

            _server.SetAsSystemHttpProxy(endPoint);
            _server.SetAsSystemHttpsProxy(endPoint);
        }

        public void Shutdown()
        {
            _server.Stop();
            _server.Dispose();
        }

        private Task BeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs args)
        {
            string hostname = args.HttpClient.Request.RequestUri.Host;
            if (_ssl_decrypt_policy == SSL_Decrypt_Policy.Never)
            {
                args.DecryptSsl = false;
            }
            else if (_ssl_decrypt_policy == SSL_Decrypt_Policy.Always)
            {
                args.DecryptSsl = true;
            }
            else
            {
                args.DecryptSsl = ShouldRedirect(hostname);
            }
            return Task.CompletedTask;
        }

        private Task OnCertValidation(object sender, CertificateValidationEventArgs args)
        {
            if (_ssl_error_policy == SSL_Error_Policy.AlwaysIgnore)
            {
                args.IsValid = true;
            }
            else
            {
                args.IsValid = args.SslPolicyErrors == SslPolicyErrors.None;
            }
            return Task.CompletedTask;
        }

        private Task BeforeRequest(object sender, SessionEventArgs args)
        {
            string hostname = args.HttpClient.Request.RequestUri.Host;
            if (ShouldRedirect(hostname))
            {
                string requestUrl = args.HttpClient.Request.Url;
                string replacedUrl = new UriBuilder(requestUrl)
                {
                    Scheme = _target_uri.Scheme,
                    Host = _target_uri.Host,
                    Port = _target_uri.Port
                }.Uri.ToString();
                if (_url_patches != null)
                {
                    foreach (var patch in _url_patches)
                    {
                        replacedUrl = replacedUrl.Replace(patch.Key, patch.Value);
                    }
                }
                args.HttpClient.Request.Url = replacedUrl;
                Console.WriteLine("Redirecting: " + requestUrl + " --> " + replacedUrl);
            }
            return Task.CompletedTask;
        }

        private bool ShouldRedirect(string hostname)
        {
            foreach (string domain in _redirect_domains)
            {
                if (string.IsNullOrWhiteSpace(domain))
                {
                    continue;
                }
                if (domain.StartsWith("*.") || domain.StartsWith('.'))
                {
                    int stripSize = domain.StartsWith("*.") ? 2 : 1;
                    if (hostname.EndsWith(domain.Remove(0, stripSize)))
                    {
                        return true;
                    }
                }
                else
                {
                    if (hostname == domain)
                    {
                        return true;
                    }
                }   
            }
            return false;
        }
    }

}