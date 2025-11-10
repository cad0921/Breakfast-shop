using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(breakfastshop.SignalRStartup))]

namespace breakfastshop
{
    public class SignalRStartup
    {
        public void Configuration(IAppBuilder app)
        {
            app.MapSignalR();
        }
    }
}

