using System.Collections.Generic;

namespace Emby.Dlna
{
    public class ControlResponse
    {
        public IDictionary<string, string> Headers { get; set; }

        public string Xml { get; set; }

        public bool IsSuccessful { get; set; }

        public ControlResponse()
        {
            Headers = new Dictionary<string, string>();
        }
    }
}