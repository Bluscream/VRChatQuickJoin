using System;
using System.Linq;
using System.Threading.Tasks;
using VRChat.API.Api;
using VRChat.API.Client;
using static Program;

namespace VRChatQuickJoin
{
    internal class VrcApiClient : ApiClient
    {
        internal VRChat.API.Client.Configuration Configuration { get; set; }
        private AuthenticationApi _Auth { get; set; }
        internal AuthenticationApi Auth { get { if (_Auth is null) _Auth = new AuthenticationApi(this, this, Configuration); return _Auth; } }
        private WorldsApi _Worlds { get; set; }
        internal WorldsApi Worlds { get { if (_Worlds is null) _Worlds = new WorldsApi(this, this, Configuration); return _Worlds; } }
        private GroupsApi _Groups { get; set; }
        internal GroupsApi Groups { get { if (_Groups is null) _Groups = new GroupsApi(this, this, Configuration); return _Groups; } }
        private UsersApi _Users { get; set; }
        internal UsersApi Users { get { if (_Users is null) _Users = new UsersApi(this, this, Configuration); return _Users; } }
        private InviteApi _Invite { get; set; }
        internal InviteApi Invite { get { if (_Invite is null) _Invite = new InviteApi(this, this, Configuration); return _Invite; } }

        public async Task<bool> TryId(string id)
        {
            if (id.StartsWith("grp_"))
            {
                return await TryGroupId(id);
            }
            else if (id.StartsWith("wrld_"))
            {
                return await TryWorldId(id);
            }
            else if (id.StartsWith("usr_"))
            {
                return await TryUserId(id);
            }
            else
            {
                Console.WriteLine($"Unknown or Unsupported id prefix: {id}");
                Console.WriteLine($"Please file an issue at {RepositoryUrl}issues/new");
                return false;
            }
        }

        private async Task<bool> TryGroupId(string groupId)
        {
            try
            {
                //Console.WriteLine($"Trying Group ID: {groupId}");
                if (cfg.App.FetchDetails)
                {
                    var group = await Groups.GetGroupAsync(groupId);
                    if (group is null)
                    {
                        Console.WriteLine($"Group {groupId} not found.");
                        return false;
                    }
                    if (cfg.App.OverwriteComments) cfg.App.Ids[groupId] = group.Name; // Update config with group name
                }
                var groupInstances = await Groups.GetGroupInstancesAsync(groupId);
                Console.WriteLine($"Found {groupInstances.Count} Group Instances");
                var instances = groupInstances.OrderByDescending(i => i.MemberCount);
                foreach (var instance in instances)
                {
                    if (instance is null) continue;
                    if (instance.MemberCount <= 0) continue; // Skip empty instances
                    if (instance.MemberCount >= instance.World.Capacity) continue; // Skip full instances
                    Console.WriteLine($"Instance: {instance.InstanceId} ({instance.MemberCount})");
                    if (cfg.App.LaunchMode == VRChatQuickJoin.Configuration.LaunchMode.SelfInvite || Utils.IsVrchatRunning())
                    {
                        await client.InviteSelf(instance.World.Id, instance.InstanceId);
                    } else
                    {
                        var joinLink = Utils.BuildJoinLink(instance.World.Id, instance.InstanceId);
                        Utils.StartGame(joinLink, args);
                    }
                    return true;
                }
                Console.WriteLine($"No matching instance found for group {groupId}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching group instances for {groupId}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryWorldId(string worldId)
        {
            try
            {
                //Console.WriteLine($"Trying World ID: {worldId}");
                var world = await Worlds.GetWorldAsync(worldId);
                if (world is null)
                {
                    Console.WriteLine($"World {worldId} not found.");
                    return false;
                }
                if (cfg.App.OverwriteComments) cfg.App.Ids[worldId] = $"{world.Name} by {world.AuthorName}"; // Update config with world name
                Console.WriteLine($"Resolved World: \"{world.Name}\" by \"{world.AuthorName}\"");
                var instances = world.Instances.OrderByDescending(i => i.Count);
                foreach (var _instance in instances)
                {
                    if (_instance is null) return false;
                    var validUserCount = int.TryParse(_instance[1].ToString(), out int userCount);
                    if (validUserCount && userCount <= 0) continue; // Skip empty instances
                    if (validUserCount && userCount >= _instance.Capacity) continue; // Skip full instances
                    var InstanceId = _instance[0].ToString();
                    var Location = $"{worldId}:{InstanceId}";
                    Console.WriteLine($"Instance: {InstanceId} ({userCount})");
                    if (cfg.App.LaunchMode == VRChatQuickJoin.Configuration.LaunchMode.SelfInvite || Utils.IsVrchatRunning())
                    {
                        await client.InviteSelf(worldId, InstanceId);
                    }
                    else
                    {
                        var joinLink = Utils.BuildJoinLink(worldId, InstanceId);
                        Utils.StartGame(joinLink, args);
                    }
                    return true;
                }
                Console.WriteLine($"No matching instance found for world {worldId}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching world instances for {worldId}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryUserId(string userId)
        {
            try
            {
                //Console.WriteLine($"Trying User ID: {userId}");
                var user = await Users.GetUserAsync(userId);
                if (user == null)
                {
                    Console.WriteLine($"User {userId} not found or not a friend.");
                    return false;
                }
                if (cfg.App.OverwriteComments) cfg.App.Ids[userId] = user.DisplayName; // Update config with user display name
                if (string.IsNullOrEmpty(user.Location) || user.Location == "traveling" || user.Location == "offline" || user.Location == "private")
                {
                    Console.WriteLine($"User {user.DisplayName} is not in a joinable location ({user.Location})");
                    return false;
                }
                Console.WriteLine($"User {user.DisplayName} is in location: {user.Location}");
                var locationParts = user.Location.Split(':');
                if (locationParts.Length < 2 || !locationParts[0].StartsWith("wrld_"))
                {
                    Console.WriteLine($"User {user.DisplayName} is not in a joinable instance");
                    return false;
                }
                var worldId = locationParts[0];
                var instanceId = locationParts[1];
                Console.WriteLine($"Joining user {user.DisplayName} at {worldId}:{instanceId}");
                if (cfg.App.LaunchMode == VRChatQuickJoin.Configuration.LaunchMode.SelfInvite || Utils.IsVrchatRunning())
                {
                    await client.InviteSelf(worldId, instanceId);
                }
                else
                {
                    var joinLink = Utils.BuildJoinLink(worldId, instanceId);
                    Utils.StartGame(joinLink, args);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching user {userId}: {ex.Message}");
                return false;
            }
        }
        internal async Task<bool> InviteSelf(string worldId, string instanceId)
        {
            Console.WriteLine($"Inviting {Users} to {worldId}:{instanceId}");
            await Invite.InviteMyselfToAsync(worldId, instanceId);
            return true;
        }
    }

    }
