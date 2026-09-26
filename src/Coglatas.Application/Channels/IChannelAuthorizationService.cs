namespace Coglatas.Application.Channels;

public interface IChannelAuthorizationService
{
    Task<bool> CanViewChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default);

    Task<bool> CanPostToChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default);

    Task<bool> CanManageChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default);

    Task<bool> CanPinPost(Guid userId, Guid postId, CancellationToken cancellationToken = default);
}
