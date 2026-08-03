namespace TeamsSync.Application.Abstractions;

/// <summary>実行時に一意な識別子を発行するサービス。</summary>
public interface IIdentifierGenerator
{
    Guid NewGuid();
}

/// <summary>実行時の一意な識別子を標準のGUIDで発行する。</summary>
public sealed class IdentifierGenerator : IIdentifierGenerator
{
    public Guid NewGuid() => Guid.NewGuid();
}
