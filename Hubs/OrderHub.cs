using Microsoft.AspNet.SignalR;
using System.Threading.Tasks;

namespace breakfastshop.Hubs
{
    public class OrderHub : Hub
    {
        private static string Normalize(string shopId)
        {
            return string.IsNullOrWhiteSpace(shopId)
                ? null
                : shopId.Trim().ToLowerInvariant();
        }

        public Task JoinShop(string shopId)
        {
            var normalized = Normalize(shopId);
            if (normalized == null)
            {
                return Task.CompletedTask;
            }

            return Groups.Add(Context.ConnectionId, normalized);
        }

        public Task LeaveShop(string shopId)
        {
            var normalized = Normalize(shopId);
            if (normalized == null)
            {
                return Task.CompletedTask;
            }

            return Groups.Remove(Context.ConnectionId, normalized);
        }
    }
}

