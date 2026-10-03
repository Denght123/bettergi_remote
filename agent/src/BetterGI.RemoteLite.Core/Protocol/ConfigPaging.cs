using System.Text.Json;
using BetterGI.RemoteLite.BetterGi;

namespace BetterGI.RemoteLite.Protocol;

public static class ConfigPaging
{
    public static RemoteConfigDto Page(RemoteConfigDto config, int offset = 0, int limit = 32, string? revision = null)
    {
        if (revision is not null && !string.Equals(revision, config.Revision, StringComparison.Ordinal))
            throw new ConfigConflictException(config.Revision);
        if (offset < 0 || offset > config.Fields.Count || limit is < 1 or > 128)
            throw new InvalidDataException("配置分页位置无效，请重新同步。");
        var count = Math.Min(limit, config.Fields.Count - offset);
        while (true)
        {
            var page = config with
            {
                Fields = config.Fields.Skip(offset).Take(count).ToArray(),
                NextFieldOffset = offset + count < config.Fields.Count ? offset + count : null,
                TotalFields = config.Fields.Count,
            };
            // Reserve space for the enclosing response inside the existing encrypted message limit.
            if (JsonSerializer.SerializeToUtf8Bytes(page, RemoteJson.Options).Length < ProtocolConstants.MaximumPlaintextBytes - 2048)
                return page;
            if (count <= 1)
                throw new InvalidDataException("这组配置内容过大，暂时无法完整传到手机。请减少该配置组的任务或选项后重新同步。");
            count /= 2;
        }
    }
}
