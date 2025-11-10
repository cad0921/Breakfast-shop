using breakfastshop.Hubs;
using Microsoft.AspNet.SignalR;
using System;

namespace breakfastshop.Services
{
    public static class OrderNotificationService
    {
        private static IHubContext GetHubContext()
        {
            return GlobalHost.ConnectionManager?.GetHubContext<OrderHub>();
        }

        private static string Normalize(Guid? shopId)
        {
            if (!shopId.HasValue || shopId.Value == Guid.Empty)
            {
                return null;
            }

            return shopId.Value.ToString().Trim().ToLowerInvariant();
        }

        private static string Normalize(string shopId)
        {
            return string.IsNullOrWhiteSpace(shopId) ? null : shopId.Trim().ToLowerInvariant();
        }

        public static void NotifyOrdersChanged(Guid? shopId)
        {
            NotifyOrdersChangedCore(Normalize(shopId));
        }

        public static void NotifyOrdersChanged(string shopId)
        {
            NotifyOrdersChangedCore(Normalize(shopId));
        }

        private static void NotifyOrdersChangedCore(string normalizedShopId)
        {
            var hubContext = GetHubContext();
            if (hubContext == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(normalizedShopId))
            {
                hubContext.Clients.All.ordersChanged();
            }
            else
            {
                hubContext.Clients.Group(normalizedShopId).ordersChanged();
            }
        }
    }
}

