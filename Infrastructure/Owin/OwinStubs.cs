using System;
using System.Collections.Generic;

namespace Microsoft.Owin
{
    /// <summary>
    /// Minimal replacement for the OWIN startup attribute to keep the sample
    /// application self-contained. It captures the startup type that will be
    /// used to configure the pipeline.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class OwinStartupAttribute : Attribute
    {
        public OwinStartupAttribute(Type startupType)
        {
            StartupType = startupType ?? throw new ArgumentNullException(nameof(startupType));
        }

        public OwinStartupAttribute(string friendlyName, Type startupType)
            : this(startupType)
        {
            FriendlyName = friendlyName;
        }

        /// <summary>
        /// Gets the human-readable name of the startup configuration.
        /// </summary>
        public string FriendlyName { get; }

        /// <summary>
        /// Gets the startup type registered for the application.
        /// </summary>
        public Type StartupType { get; }
    }
}

namespace Owin
{
    /// <summary>
    /// Lightweight version of the OWIN application builder interface. Only the
    /// members that are required by the project are included.
    /// </summary>
    public interface IAppBuilder
    {
        IDictionary<string, object> Properties { get; }
    }

    /// <summary>
    /// Basic implementation of the <see cref="IAppBuilder"/> interface that can
    /// be used by unit tests or other infrastructure within the sample. It
    /// behaves as a simple property bag.
    /// </summary>
    public sealed class AppBuilderStub : IAppBuilder
    {
        public IDictionary<string, object> Properties { get; } =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extension methods used by the application during startup. These are
    /// trimmed-down shims that allow the code to compile without the external
    /// OWIN dependencies.
    /// </summary>
    public static class OwinExtensions
    {
        /// <summary>
        /// Registers the SignalR hubs with the (stubbed) application builder.
        /// The method is intentionally a no-op because the in-memory SignalR
        /// implementation does not require additional setup.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The same application builder instance for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="app" /> is <c>null</c>.
        /// </exception>
        public static IAppBuilder MapSignalR(this IAppBuilder app)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app;
        }
    }
}
