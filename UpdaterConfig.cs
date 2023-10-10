using System.Text.Json.Nodes;

namespace MowerUpdater;

internal class UpdaterConfig
{
    public const string DefaultConfigJson =
"""
{
    "mirror": "https://mower.zhaozuohong.vip",
    "channels": [
        { "name": "alpha", "enable": false }
    ],
    "ignores": [
        "/*.yml",
        "/*.json",
        "tmp/",
        "log/",
        "screenshot/",
        "adb-buildin/"
    ],
    "rsync_base_addr": "/mower",
    "rsync_parameters": [
        "--partial",
        "--checksum",
        "--checksum-choice=xxh3",
        "--delete",
        "-rptv"
    ],
    "install_dir": "",
    "dir_name": "mower"
}
""";
    public static readonly JsonNode DefaultConfig = JsonNode.Parse(DefaultConfigJson);
}
