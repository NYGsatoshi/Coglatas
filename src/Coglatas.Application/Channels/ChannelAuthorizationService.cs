using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Groups;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Channels;

public sealed class ChannelAuthorizationService(
    IChannelRepository channels,
    IGroupRepository groups,
    IGroupAuthorizationService groupAuthorization) : IChannelAuthorizationService
{
    public async Task<bool> CanViewChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default)
    {
        var channel = await channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null || channel.Status != ChannelStatus.Active)
        {
            return false;
        }

        if (await groupAuthorization.CanManageGroup(userId, channel.GroupId, cancellationToken))
        {
            return true;
        }

        return channel.Type switch
        {
            ChannelType.Public or ChannelType.Announcement => await groups.GetMemberAsync(channel.GroupId, userId, cancellationToken) is not null,
            ChannelType.Private or ChannelType.Confidential => await channels.GetMemberAsync(channelId, userId, cancellationToken) is not null,
            _ => false
        };
    }

    public async Task<bool> CanPostToChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default)
    {
        var channel = await channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null || !await CanViewChannel(userId, channelId, cancellationToken))
        {
            return false;
        }

        if (await groupAuthorization.CanManageGroup(userId, channel.GroupId, cancellationToken))
        {
            return true;
        }

        var groupMember = await groups.GetMemberAsync(channel.GroupId, userId, cancellationToken);
        var channelMember = await channels.GetMemberAsync(channelId, userId, cancellationToken);

        return channel.Type switch
        {
            ChannelType.Announcement => false,
            ChannelType.Public => groupMember?.Role.CanContribute() == true,
            ChannelType.Private or ChannelType.Confidential => channelMember?.Role.CanPost() == true,
            _ => false
        };
    }

    public async Task<bool> CanManageChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default)
    {
        var channel = await channels.GetByIdAsync(channelId, cancellationToken);
        if (channel is null)
        {
            return false;
        }

        if (await groupAuthorization.CanManageGroup(userId, channel.GroupId, cancellationToken))
        {
            return true;
        }

        var member = await channels.GetMemberAsync(channelId, userId, cancellationToken);
        return member?.Role.CanManage() == true;
    }

    public async Task<bool> CanPinPost(Guid userId, Guid postId, CancellationToken cancellationToken = default)
    {
        var post = await channels.GetPostByIdAsync(postId, cancellationToken);
        return post is not null && await CanManageChannel(userId, post.ChannelId, cancellationToken);
    }
}
