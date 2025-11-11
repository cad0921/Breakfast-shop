using System;
using Microsoft.AspNet.SignalR;

namespace breakfastshop.Hubs
{
    public class OrdersHub : Hub
    {
        private static void Broadcast(string changeType, Guid orderId, Guid? shopId, string status)
        {
            var context = GlobalHost.ConnectionManager.GetHubContext<OrdersHub>();
            context.Clients.All.orderChanged(new
            {
                type = changeType,
                orderId,
                shopId,
                status
            });
        }

        public static void NotifyOrderCreated(Guid orderId, Guid? shopId)
        {
            Broadcast("created", orderId, shopId, null);
        }

        public static void NotifyOrderStatusChanged(Guid orderId, Guid? shopId, string status)
        {
            Broadcast("statusChanged", orderId, shopId, status);
        }
    }
}
