﻿// ==========================================================================
//  Startup.cs
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex Group
//  All rights reserved.
// ==========================================================================

using Microsoft.AspNetCore.Builder;
using Squidex.Areas.Portal.Middlewares;
using Squidex.Config;

namespace Squidex.Areas.Portal
{
    public static class Startup
    {
        public static void ConfigurePortal(this IApplicationBuilder app)
        {
            app.Map(Constants.PortalPrefix, portalApp =>
            {
                portalApp.UseAuthentication();
                portalApp.UseMiddleware<PortalDashboardAuthenticationMiddleware>();
                portalApp.UseMiddleware<PortalRedirectMiddleware>();
            });
        }
    }
}
