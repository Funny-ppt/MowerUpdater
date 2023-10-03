using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MowerUpdater
{
    internal interface IDepInstaller
    {
        string Name { get; }
        string Version { get; }
        public bool CheckIfInstalled();
        public Task Install(HttpClient client, CancellationToken token = default);
    }
}
