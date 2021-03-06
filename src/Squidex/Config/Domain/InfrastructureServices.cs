﻿// ==========================================================================
//  InfrastructureServices.cs
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex Group
//  All rights reserved.
// ==========================================================================

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NodaTime;
using Squidex.Infrastructure;
using Squidex.Infrastructure.Assets;
using Squidex.Infrastructure.Assets.ImageSharp;
using Squidex.Infrastructure.CQRS.Commands;
using Squidex.Infrastructure.CQRS.Events;
using Squidex.Infrastructure.Log;
using Squidex.Infrastructure.UsageTracking;
using Squidex.Pipeline;

namespace Squidex.Config.Domain
{
    public static class InfrastructureServices
    {
        public static void AddMyInfrastructureServices(this IServiceCollection services, IConfiguration config)
        {
            if (config.GetValue<bool>("logging:human"))
            {
                services.AddSingletonAs(c => new Func<IObjectWriter>(() => new JsonLogWriter(Formatting.Indented, true)));
            }
            else
            {
                services.AddSingletonAs(c => new Func<IObjectWriter>(() => new JsonLogWriter()));
            }

            var loggingFile = config.GetValue<string>("logging:file");

            if (!string.IsNullOrWhiteSpace(loggingFile))
            {
                services.AddSingletonAs(new FileChannel(loggingFile))
                    .As<ILogChannel>()
                    .As<IExternalSystem>();
            }

            services.AddSingletonAs(c => new ApplicationInfoLogAppender(typeof(Program).Assembly, Guid.NewGuid()))
                .As<ILogAppender>();

            services.AddSingletonAs<ActionContextLogAppender>()
                .As<ILogAppender>();

            services.AddSingletonAs<TimestampLogAppender>()
                .As<ILogAppender>();

            services.AddSingletonAs<DebugLogChannel>()
                .As<ILogChannel>();

            services.AddSingletonAs<ConsoleLogChannel>()
                .As<ILogChannel>();

            services.AddSingletonAs<SemanticLog>()
                .As<ISemanticLog>();

            services.AddSingletonAs(SystemClock.Instance)
                .As<IClock>();

            services.AddSingletonAs<BackgroundUsageTracker>()
                .As<IUsageTracker>();

            services.AddSingletonAs<HttpContextAccessor>()
                .As<IHttpContextAccessor>();

            services.AddSingletonAs<ActionContextAccessor>()
                .As<IActionContextAccessor>();

            services.AddSingletonAs<DefaultDomainObjectRepository>()
                .As<IDomainObjectRepository>();

            services.AddSingletonAs<DefaultDomainObjectFactory>()
                .As<IDomainObjectFactory>();

            services.AddSingletonAs<AggregateHandler>()
                .As<IAggregateHandler>();

            services.AddSingletonAs<InMemoryCommandBus>()
                .As<ICommandBus>();

            services.AddSingletonAs<DefaultStreamNameResolver>()
                .As<IStreamNameResolver>();

            services.AddSingletonAs<ImageSharpAssetThumbnailGenerator>()
                .As<IAssetThumbnailGenerator>();

            services.AddSingletonAs<EventDataFormatter>();
        }
    }
}
