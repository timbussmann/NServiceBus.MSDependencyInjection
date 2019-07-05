﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder.Common;

namespace NServiceBus.ObjectBuilder.MSDependencyInjection
{
    internal class ServicesObjectBuilder : IContainer
    {
        private bool _isChild;
        private static ILog s_logger = LogManager.GetLogger<ServicesObjectBuilder>();
        private readonly bool _owned;
        private readonly UpdateableServiceProvider _runtimeServiceProvider;
        private readonly IServiceCollection _services;
        private readonly IServiceScope _scope;

        public ServicesObjectBuilder(Func<IServiceCollection, UpdateableServiceProvider> serviceProviderFactory) : this(new ServiceCollection(), true, serviceProviderFactory)
        {
        }

        public ServicesObjectBuilder(IServiceCollection services, Func<IServiceCollection, UpdateableServiceProvider> serviceProviderFactory) : this(services, false, serviceProviderFactory)
        {
        }

        public ServicesObjectBuilder(IServiceCollection services, bool owned, Func<IServiceCollection, UpdateableServiceProvider> serviceProviderFactory)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services), "The object builder must be initialized with a valid service collection instance.");

            _owned = owned;
            _services = services;
            _runtimeServiceProvider = serviceProviderFactory(_services);
        }

        private ServicesObjectBuilder(bool owned, UpdateableServiceProvider updateableServiceProvider)
        {
            _owned = owned;
            _runtimeServiceProvider = updateableServiceProvider;
            _scope = _runtimeServiceProvider.CreateScope();
        }

        public void Dispose()
        {
            //Injected at compile time
        }

        public void DisposeManaged()
        {
            //if we are in a child scope dispose of that but not the parent container
            if (!_isChild && _owned)
            {
                _runtimeServiceProvider.Dispose();
            }

            if (_owned)
            {
                (_services as IDisposable)?.Dispose();
            }
            _scope?.Dispose();
        }

        public IContainer BuildChildContainer()
        {
            return new ServicesObjectBuilder(false, _runtimeServiceProvider)
            {
                _isChild = true,
            };
        }

        public void Configure(Type component, DependencyLifecycle dependencyLifecycle)
        {
            ThrowIfCalledOnChildContainer();

            if (HasComponent(component))
            {
                s_logger.Info("Component " + component.FullName + " was already registered in the container.");
                return;
            }

            var lifestyle = GetLifetimeTypeFrom(dependencyLifecycle);
            var services = GetAllServiceTypesFor(component);

            _runtimeServiceProvider.AddServices(services, component, lifestyle);
        }

        public void Configure<T>(Func<T> component, DependencyLifecycle dependencyLifecycle)
        {
            ThrowIfCalledOnChildContainer();

            var componentType = typeof(T);

            if (HasComponent(componentType))
            {
                s_logger.Info("Component " + componentType.FullName + " was already registered in the container.");
                return;
            }

            var lifestyle = GetLifetimeTypeFrom(dependencyLifecycle);
            var services = GetAllServiceTypesFor(componentType);

            _runtimeServiceProvider.AddServices(services, component, lifestyle);
        }

        public void RegisterSingleton(Type lookupType, object instance)
        {
            ThrowIfCalledOnChildContainer();

            var serviceDescriptor = _runtimeServiceProvider.FirstOrDefault(d => d.ServiceType == lookupType);

            if (serviceDescriptor != null)
                _runtimeServiceProvider.Remove(serviceDescriptor);

            _runtimeServiceProvider.AddSingleton(lookupType, instance);
        }

        public object Build(Type typeToBuild)
        {
            if (_scope != null)
                return _scope.ServiceProvider.GetService(typeToBuild);

            return _runtimeServiceProvider.GetService(typeToBuild);
        }

        public IEnumerable<object> BuildAll(Type typeToBuild)
        {
            if (_scope != null)
                return _scope.ServiceProvider.GetServices(typeToBuild);

            return _runtimeServiceProvider.GetServices(typeToBuild);
        }

        public bool HasComponent(Type componentType)
        {
            return _runtimeServiceProvider.Any(d => d.ServiceType == componentType);
        }

        public void Release(object instance)
        {
            // no release logic
        }

        void ThrowIfCalledOnChildContainer()
        {
            if (_isChild)
            {
                throw new InvalidOperationException("Reconfiguration of child containers is not allowed.");
            }
        }

        static ServiceLifetime GetLifetimeTypeFrom(DependencyLifecycle dependencyLifecycle)
        {
            switch (dependencyLifecycle)
            {
                case DependencyLifecycle.InstancePerCall:
                    return ServiceLifetime.Transient;
                case DependencyLifecycle.SingleInstance:
                    return ServiceLifetime.Singleton;
                case DependencyLifecycle.InstancePerUnitOfWork:
                    return ServiceLifetime.Scoped;
            }

            throw new ArgumentException("Unhandled lifecycle - " + dependencyLifecycle);
        }

        static IEnumerable<Type> GetAllServiceTypesFor(Type t)
        {
            return t.GetInterfaces()
                .Where(x => !x.FullName.StartsWith("System."))
                .Concat(new[] { t });
        }
    }
}
