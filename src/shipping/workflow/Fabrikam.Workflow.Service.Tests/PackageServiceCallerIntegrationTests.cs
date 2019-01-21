﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Fabrikam.Workflow.Service.Models;
using Fabrikam.Workflow.Service.Services;
using Fabrikam.Workflow.Service.Tests.Utils;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Fabrikam.Workflow.Service.Tests
{
    public class PackageServiceCallerIntegrationTests : IDisposable
    {
        private const string PackageHost = "packagehost";
        private static readonly string PackageUri = $"http://{PackageHost}/api/packages/";

        private readonly TestServer _testServer;
        private RequestDelegate _handleHttpRequest = ctx => Task.CompletedTask;

        private readonly IPackageServiceCaller _caller;

        public PackageServiceCallerIntegrationTests()
        {
            var context = new HostBuilderContext(new Dictionary<object, object>());
            context.Configuration =
                new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string>
                    {
                        ["SERVICE_URI_PACKAGE"] = PackageUri
                    }).Build();
            context.HostingEnvironment =
                Mock.Of<Microsoft.Extensions.Hosting.IHostingEnvironment>(e => e.EnvironmentName == "Test");

            var serviceCollection = new ServiceCollection();
            ServiceStartup.ConfigureServices(context, serviceCollection);
            serviceCollection.AddLogging(builder => builder.AddDebug());

            _testServer =
                new TestServer(
                    new WebHostBuilder()
                        .Configure(builder =>
                        {
                            builder.UseMvc();
                            builder.Run(ctx => _handleHttpRequest(ctx));
                        })
                        .ConfigureServices(builder =>
                        {
                            builder.AddMvc();
                        }));

            serviceCollection.Replace(
                ServiceDescriptor.Transient<HttpMessageHandlerBuilder, TestServerMessageHandlerBuilder>(
                    sp => new TestServerMessageHandlerBuilder(_testServer)));
            var serviceProvider = serviceCollection.BuildServiceProvider();

            _caller = serviceProvider.GetService<IPackageServiceCaller>();
        }

        public void Dispose()
        {
            _testServer.Dispose();
        }

        [Fact]
        public async Task WhenCreatingPackage_ThenInvokesDroneSchedulerAPI()
        {
            string actualPackageId = null;
            PackageGen actualPackage = null;
            _handleHttpRequest = ctx =>
            {
                if (ctx.Request.Host.Host == PackageHost)
                {
                    actualPackageId = ctx.Request.Path;
                    actualPackage =
                        new JsonSerializer().Deserialize<PackageGen>(new JsonTextReader(new StreamReader(ctx.Request.Body, Encoding.UTF8)));
                    ctx.Response.StatusCode = (int)HttpStatusCode.Created;
                }
                else
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }

                return Task.CompletedTask;
            };

            var packageInfo = new PackageInfo { PackageId = "somePackageId", Size = ContainerSize.Medium, Tag = "sometag", Weight = 100d };
            await _caller.CreatePackageAsync(packageInfo);

            Assert.NotNull(actualPackageId);
            Assert.Equal($"/api/packages/{packageInfo.PackageId}", actualPackageId);

            Assert.NotNull(actualPackage);
            Assert.Equal((int)packageInfo.Size, (int)actualPackage.Size);
            Assert.Equal(packageInfo.Tag, actualPackage.Tag);
            Assert.Equal(packageInfo.Weight, actualPackage.Weight);
        }

        [Fact]
        public async Task WhenPackageAPIReturnsOK_ThenReturnsGeneratedPackage()
        {
            _handleHttpRequest = async ctx =>
            {
                if (ctx.Request.Host.Host == PackageHost)
                {
                    await ctx.WriteResultAsync(
                        new ObjectResult(
                            new PackageGen { Id = "somePackageId", Size = ContainerSize.Medium, Tag = "sometag", Weight = 100d })
                        {
                            StatusCode = (int)HttpStatusCode.Created
                        });
                }
                else
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }
            };

            var packageInfo = new PackageInfo { PackageId = "somePackageId", Size = ContainerSize.Medium, Tag = "sometag", Weight = 100d };
            var actualPackage = await _caller.CreatePackageAsync(packageInfo);

            Assert.NotNull(actualPackage);
            Assert.Equal((int)packageInfo.Size, (int)actualPackage.Size);
            Assert.Equal(packageInfo.Tag, actualPackage.Tag);
            Assert.Equal(packageInfo.Weight, actualPackage.Weight);
        }

        [Fact]
        public async Task WhenPackageAPIDoesNotReturnOK_ThenThrows()
        {
            _handleHttpRequest = ctx =>
            {
                if (ctx.Request.Host.Host == PackageHost)
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                }
                else
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }

                return Task.CompletedTask;
            };

            var packageInfo = new PackageInfo { PackageId = "somePackageId", Size = ContainerSize.Medium, Tag = "sometag", Weight = 100d };

            await Assert.ThrowsAsync<BackendServiceCallFailedException>(() => _caller.CreatePackageAsync(packageInfo));
        }
    }
}

