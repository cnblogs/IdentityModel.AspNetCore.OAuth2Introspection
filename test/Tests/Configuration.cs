﻿// Copyright (c) Duende Software. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Duende.IdentityModel.Client;
using FluentAssertions;
using IdentityModel.AspNetCore.OAuth2Introspection;
using System;
using System.Threading.Tasks;
using Tests.Util;
using Xunit;

namespace Tests
{
    public class Configuration
    {
        [Fact]
        public void Empty_Options()
        {
            Action act = () => PipelineFactory.CreateClient(options => { })
                .GetAsync("http://test").GetAwaiter().GetResult();

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("You must either set Authority or IntrospectionEndpoint");
        }

        [Fact]
        public void No_Token_Retriever()
        {
            Action act = () => PipelineFactory.CreateClient(options =>
            {
                options.Authority = "http://foo";
                options.ClientId = "scope";
                options.TokenRetriever = null;
            }).GetAsync("http://test").GetAwaiter().GetResult();

            act.Should().Throw<ArgumentException>()
                .Where(e => e.Message.StartsWith("TokenRetriever must be set"));
        }

        [Fact]
        public void Endpoint_But_No_Authority()
        {
            Action act = () => PipelineFactory.CreateClient(options =>
            {
                options.IntrospectionEndpoint = "http://endpoint";
                options.ClientId = "scope";

            }).GetAsync("http://test").GetAwaiter().GetResult();

            act.Should().NotThrow();
        }

        [Fact]
        public void Caching_With_Caching_Service()
        {
            Action act = () => PipelineFactory.CreateClient(options =>
            {
                options.IntrospectionEndpoint = "http://endpoint";
                options.ClientId = "scope";
                options.EnableCaching = true;

            }, addCaching: true).GetAsync("http://test").GetAwaiter().GetResult();

            act.Should().NotThrow();
        }

        [Fact]
        public void Caching_Without_Caching_Service()
        {
            Action act = () => PipelineFactory.CreateClient(options =>
            {
                options.IntrospectionEndpoint = "http://endpoint";
                options.ClientId = "scope";
                options.EnableCaching = true;

            }).GetAsync("http://test").GetAwaiter().GetResult();

            act.Should().Throw<ArgumentException>()
                .Where(e => e.Message.StartsWith("Caching is enabled, but no IDistributedCache is found in the services collection"));
        }

        [Fact]
        public void No_ClientName_But_Introspection_Handler()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);

            Action act = () => PipelineFactory.CreateClient(options =>
            {
                options.IntrospectionEndpoint = "http://endpoint";
            }, handler).GetAsync("http://test").GetAwaiter().GetResult();

            act.Should().NotThrow();
        }

        [Fact]
        public void Authority_No_Network_Delay_Load()
        {
            Action act = () => PipelineFactory.CreateClient(options =>
            {
                options.Authority = "http://localhost:6666";
                options.ClientId = "scope";
            }).GetAsync("http://test").GetAwaiter().GetResult();

            act.Should().NotThrow();
        }

        [Fact]
        public async Task Authority_Get_Introspection_Endpoint()
        {
            OAuth2IntrospectionOptions ops = null;
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);

            var client = PipelineFactory.CreateClient(options =>
            {
                options.Authority = "https://authority.com/";
                options.ClientId = "scope";

                options.DiscoveryPolicy.RequireKeySet = false;
                ops = options;
            }, handler);

            client.SetBearerToken("token");
            await client.GetAsync("http://server/api");

            ops.IntrospectionEndpoint.Should().Be("https://authority.com/introspection_endpoint");
        }
    }
}