using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.AspNet.SignalR
{
    /// <summary>
    /// Provides a lightweight, in-memory SignalR implementation that is sufficient for
    /// unit testing and running the sample application without the full ASP.NET SignalR
    /// runtime. It supports group membership tracking and basic client invocation.
    /// </summary>
    public static class GlobalHost
    {
        private static IConnectionManager _connectionManager = new InMemoryConnectionManager();

        public static IConnectionManager ConnectionManager
        {
            get => _connectionManager;
            set => _connectionManager = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    public interface IConnectionManager
    {
        IHubContext GetHubContext<THub>() where THub : Hub;
    }

    public interface IHubContext
    {
        dynamic Clients { get; }
    }

    public interface IGroupManager
    {
        Task Add(string connectionId, string groupName);
        Task Remove(string connectionId, string groupName);
    }

    public abstract class Hub
    {
        private readonly HubLifetimeManager _lifetimeManager;

        protected Hub()
        {
            Context = new HubCallerContext
            {
                ConnectionId = Guid.NewGuid().ToString("N")
            };
            _lifetimeManager = HubLifetimeManagerStore.GetManager(GetType());
            Groups = new GroupManager(_lifetimeManager);
        }

        public HubCallerContext Context { get; }

        public IGroupManager Groups { get; }

        internal HubLifetimeManager LifetimeManager => _lifetimeManager;
    }

    public class HubCallerContext
    {
        public string ConnectionId { get; set; }
    }

    internal static class HubLifetimeManagerStore
    {
        private static readonly ConcurrentDictionary<Type, HubLifetimeManager> Managers =
            new ConcurrentDictionary<Type, HubLifetimeManager>();

        public static HubLifetimeManager GetManager(Type hubType)
        {
            if (hubType == null) throw new ArgumentNullException(nameof(hubType));
            return Managers.GetOrAdd(hubType, _ => new HubLifetimeManager());
        }
    }

    internal sealed class HubLifetimeManager
    {
        private readonly ConcurrentDictionary<string, ClientConnection> _connections =
            new ConcurrentDictionary<string, ClientConnection>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, HashSet<string>> _groups =
            new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Action<string, object[]>> _allHandlers = new List<Action<string, object[]>>();
        private readonly object _allLock = new object();

        public IDisposable RegisterAllHandler(Action<string, object[]> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_allLock)
            {
                _allHandlers.Add(handler);
            }

            return new DisposableAction(() =>
            {
                lock (_allLock)
                {
                    _allHandlers.Remove(handler);
                }
            });
        }

        public IDisposable RegisterConnectionHandler(string connectionId, Action<string, object[]> handler)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                throw new ArgumentException("Connection id cannot be empty", nameof(connectionId));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var connection = _connections.GetOrAdd(connectionId, id => new ClientConnection(id));
            return connection.RegisterHandler(handler);
        }

        public IDisposable RegisterGroupHandler(string groupName, Action<string, object[]> handler)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Group name cannot be empty", nameof(groupName));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var key = groupName.Trim();
            var connection = _connections.GetOrAdd("__group:" + key, id => new ClientConnection(id));
            return connection.RegisterHandler(handler);
        }

        public void AddToGroup(string connectionId, string groupName)
        {
            if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(groupName))
            {
                return;
            }

            var normalizedGroup = groupName.Trim();
            var connection = _connections.GetOrAdd(connectionId, id => new ClientConnection(id));
            connection.AddGroup(normalizedGroup);

            var members = _groups.GetOrAdd(normalizedGroup, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            lock (members)
            {
                members.Add(connectionId);
            }
        }

        public void RemoveFromGroup(string connectionId, string groupName)
        {
            if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(groupName))
            {
                return;
            }

            var normalizedGroup = groupName.Trim();
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                connection.RemoveGroup(normalizedGroup);
            }

            if (_groups.TryGetValue(normalizedGroup, out var members))
            {
                lock (members)
                {
                    members.Remove(connectionId);
                    if (members.Count == 0)
                    {
                        _groups.TryRemove(normalizedGroup, out _);
                    }
                }
            }
        }

        public ClientProxy CreateAllProxy()
        {
            return new ClientProxy(BroadcastToAll);
        }

        public ClientProxy CreateGroupProxy(string groupName)
        {
            return new ClientProxy((method, args) => BroadcastToGroup(groupName, method, args));
        }

        private void BroadcastToAll(string methodName, object[] args)
        {
            Action<string, object[]>[] handlers;
            lock (_allLock)
            {
                handlers = _allHandlers.ToArray();
            }

            foreach (var handler in handlers)
            {
                handler(methodName, args);
            }

            foreach (var connection in _connections.Values.ToArray())
            {
                connection.Invoke(methodName, args);
            }
        }

        private void BroadcastToGroup(string groupName, string methodName, object[] args)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return;
            }

            var normalized = groupName.Trim();

            if (_groups.TryGetValue(normalized, out var members))
            {
                string[] snapshot;
                lock (members)
                {
                    snapshot = members.ToArray();
                }

                foreach (var connectionId in snapshot)
                {
                    if (_connections.TryGetValue(connectionId, out var connection))
                    {
                        connection.Invoke(methodName, args);
                    }
                }
            }

            if (_connections.TryGetValue("__group:" + normalized, out var groupConnection))
            {
                groupConnection.Invoke(methodName, args);
            }
        }
    }

    internal sealed class ClientProxy : DynamicObject
    {
        private readonly Action<string, object[]> _invoker;

        public ClientProxy(Action<string, object[]> invoker)
        {
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            result = null;
            _invoker(binder?.Name ?? string.Empty, args ?? Array.Empty<object>());
            return true;
        }
    }

    internal sealed class GroupManager : IGroupManager
    {
        private readonly HubLifetimeManager _lifetimeManager;

        public GroupManager(HubLifetimeManager lifetimeManager)
        {
            _lifetimeManager = lifetimeManager ?? throw new ArgumentNullException(nameof(lifetimeManager));
        }

        public Task Add(string connectionId, string groupName)
        {
            _lifetimeManager.AddToGroup(connectionId, groupName);
            return Task.CompletedTask;
        }

        public Task Remove(string connectionId, string groupName)
        {
            _lifetimeManager.RemoveFromGroup(connectionId, groupName);
            return Task.CompletedTask;
        }
    }

    internal sealed class InMemoryConnectionManager : IConnectionManager
    {
        public IHubContext GetHubContext<THub>() where THub : Hub
        {
            var manager = HubLifetimeManagerStore.GetManager(typeof(THub));
            return new InMemoryHubContext(manager);
        }
    }

    public interface IInMemoryHubContext : IHubContext
    {
        IDisposable RegisterAllHandler(Action<string, object[]> handler);
        IDisposable RegisterConnectionHandler(string connectionId, Action<string, object[]> handler);
        IDisposable RegisterGroupHandler(string groupName, Action<string, object[]> handler);
    }

    internal sealed class InMemoryHubContext : IInMemoryHubContext
    {
        private readonly HubLifetimeManager _lifetimeManager;
        private readonly HubClients _clients;

        public InMemoryHubContext(HubLifetimeManager lifetimeManager)
        {
            _lifetimeManager = lifetimeManager ?? throw new ArgumentNullException(nameof(lifetimeManager));
            _clients = new HubClients(lifetimeManager);
        }

        public dynamic Clients => _clients;

        public IDisposable RegisterAllHandler(Action<string, object[]> handler)
        {
            return _lifetimeManager.RegisterAllHandler(handler);
        }

        public IDisposable RegisterConnectionHandler(string connectionId, Action<string, object[]> handler)
        {
            return _lifetimeManager.RegisterConnectionHandler(connectionId, handler);
        }

        public IDisposable RegisterGroupHandler(string groupName, Action<string, object[]> handler)
        {
            return _lifetimeManager.RegisterGroupHandler(groupName, handler);
        }
    }

    internal sealed class HubClients : DynamicObject
    {
        private readonly HubLifetimeManager _lifetimeManager;

        public HubClients(HubLifetimeManager lifetimeManager)
        {
            _lifetimeManager = lifetimeManager ?? throw new ArgumentNullException(nameof(lifetimeManager));
        }

        public dynamic All => _lifetimeManager.CreateAllProxy();

        public dynamic Group(string groupName)
        {
            return _lifetimeManager.CreateGroupProxy(groupName);
        }
    }

    internal sealed class ClientConnection
    {
        private readonly List<Action<string, object[]>> _handlers = new List<Action<string, object[]>>();
        private readonly object _lock = new object();

        public ClientConnection(string connectionId)
        {
            ConnectionId = connectionId;
        }

        public string ConnectionId { get; }

        public HashSet<string> Groups { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IDisposable RegisterHandler(Action<string, object[]> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_lock)
            {
                _handlers.Add(handler);
            }

            return new DisposableAction(() =>
            {
                lock (_lock)
                {
                    _handlers.Remove(handler);
                }
            });
        }

        public void Invoke(string methodName, object[] args)
        {
            Action<string, object[]>[] handlers;
            lock (_lock)
            {
                handlers = _handlers.ToArray();
            }

            foreach (var handler in handlers)
            {
                handler(methodName, args);
            }
        }

        public void AddGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return;
            }

            lock (_lock)
            {
                Groups.Add(groupName.Trim());
            }
        }

        public void RemoveGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return;
            }

            lock (_lock)
            {
                Groups.Remove(groupName.Trim());
            }
        }
    }

    internal sealed class DisposableAction : IDisposable
    {
        private readonly Action _dispose;
        private bool _disposed;

        public DisposableAction(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _dispose?.Invoke();
        }
    }
}
