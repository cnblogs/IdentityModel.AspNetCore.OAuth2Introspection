﻿// Copyright (c) Duende Software. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using FluentAssertions;
using IdentityModel;
using IdentityModel.AspNetCore.OAuth2Introspection;
using IdentityModel.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Tests.Util;
using Xunit;

namespace Tests
{
    public class Introspection
    {
        private static readonly string clientId = "client";
        private static readonly string clientSecret = "secret";

        private readonly Action<OAuth2IntrospectionOptions> _options = o =>
        {
            o.Authority = "https://authority.com";
            o.DiscoveryPolicy.RequireKeySet = false;

            o.ClientId = clientId;
            o.ClientSecret = clientSecret;
        };

        [Fact]
        public async Task Unauthorized_Client()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Unauthorized);

            var client = PipelineFactory.CreateClient(o => _options(o), handler);
            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ActiveToken()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);

            var client = PipelineFactory.CreateClient(_options, handler);
            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);

            var request = handler.LastRequest;
            request.Should().ContainKey("client_id").WhichValue.Should().Be(clientId);
            request.Should().ContainKey("client_secret").WhichValue.Should().Be(clientSecret);
        }

        [Theory]
        [InlineData(5000, "testAssertion1", "testAssertion1")]
        [InlineData(-5000, "testAssertion1", "testAssertion2")]
        public async Task ActiveToken_With_ClientAssertion(int ttl, string assertion1, string assertion2)
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);
            var count = 0;

            var client = PipelineFactory.CreateClient(o =>
            {
                _options(o);
                o.ClientSecret = null;

                o.Events.OnUpdateClientAssertion = e =>
                {
                    count++;
                    e.ClientAssertion = new ClientAssertion
                    {
                        Type = "testType",
                        Value = "testAssertion" + count
                    };
                    e.ClientAssertionExpirationTime = DateTime.UtcNow.AddMilliseconds(ttl);

                    return Task.CompletedTask;
                };
            }, handler);

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);

            var request = handler.LastRequest;
            request.Should().ContainKey("client_id").WhichValue.Should().Be(clientId);
            request.Should().ContainKey("client_assertion_type").WhichValue.Should().Be("testType");
            request.Should().ContainKey("client_assertion").WhichValue.Should().Be(assertion1);

            result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);

            request = handler.LastRequest;
            request.Should().ContainKey("client_id").WhichValue.Should().Be(clientId);
            request.Should().ContainKey("client_assertion_type").WhichValue.Should().Be("testType");
            request.Should().ContainKey("client_assertion").WhichValue.Should().Be(assertion2);
        }

        [Fact]
        public async Task Active_token_with_inline_event_events_should_be_called()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);
            bool? validatedCalled = null;
            bool? failureCalled = null;

            var client = PipelineFactory.CreateClient(o =>
            {
                _options(o);

                o.Events.OnTokenValidated = e =>
                {
                    validatedCalled = true;

                    return Task.CompletedTask;
                };

                o.Events.OnAuthenticationFailed = e =>
                {
                    failureCalled = true;

                    return Task.CompletedTask;
                };

            }, handler);

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");

            result.StatusCode.Should().Be(HttpStatusCode.OK);
            validatedCalled.Should().BeTrue();
            failureCalled.Should().BeNull();
        }

        [Fact]
        public async Task ActiveToken_With_Caching_Ttl_Longer_Than_Duration()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active, TimeSpan.FromHours(1));
            var client = PipelineFactory.CreateClient(o =>
            {
                _options(o);

                o.EnableCaching = true;
                o.CacheDuration = TimeSpan.FromMinutes(10);

            }, handler, true);

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);

            result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ActiveToken_With_Caching_Ttl_Shorter_Than_Duration()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active, TimeSpan.FromMinutes(5));

            var client = PipelineFactory.CreateClient(o =>
            {
                _options(o);

                o.EnableCaching = true;
                o.CacheDuration = TimeSpan.FromMinutes(10);
            }, handler, true);

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);

            result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task InactiveToken()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Inactive);

            var client = PipelineFactory.CreateClient(o => _options(o), handler);
            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task InActive_token_with_inline_event_events_should_be_called()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Inactive);
            bool? validatedCalled = null;
            bool? failureCalled = null;

            var client = PipelineFactory.CreateClient(o =>
            {
                _options(o);

                o.Events.OnTokenValidated = e =>
                {
                    validatedCalled = true;

                    return Task.CompletedTask;
                };

                o.Events.OnAuthenticationFailed = e =>
                {
                    failureCalled = true;

                    return Task.CompletedTask;
                };

            }, handler);

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");

            result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            validatedCalled.Should().BeNull();
            failureCalled.Should().BeTrue();
        }

        [Fact]
        public async Task ActiveToken_With_SavedToken()
        {
            var expectedToken = "expected_token";
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);

            var client = PipelineFactory.CreateClient(o =>
            {
                _options(o);

                o.SaveToken = true;
            }, handler);

            client.SetBearerToken(expectedToken);

            var response = await client.GetAsync("http://test");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var responseDataStr = await response.Content.ReadAsStringAsync();
            var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseDataStr);

            responseData.Should().Contain("token", expectedToken);
        }

        [Fact]
        public async Task ActiveToken_With_SavedToken_And_Caching()
        {
            var expectedToken = "expected_token";
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active, TimeSpan.FromHours(1));

            var server = PipelineFactory.CreateServer(o =>
            {
                _options(o);

                o.SaveToken = true;
                o.EnableCaching = true;
                o.CacheDuration = TimeSpan.FromMinutes(10);
            }, handler, true);
            var client = server.CreateClient();
            client.SetBearerToken(expectedToken);

            var firstResponse = await client.GetAsync("http://test");
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var secondResponse = await client.GetAsync("http://test");
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var responseDataStr = await secondResponse.Content.ReadAsStringAsync();
            var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseDataStr);

            responseData.Should().Contain("token", expectedToken);
            AssertCacheItemExists(server, string.Empty, expectedToken);
        }

        [Fact]
        public async Task ActiveToken_With_SavedToken_And_Caching_With_Cache_Key_Prefix()
        {
            var expectedToken = "expected_token";
            var cacheKeyPrefix = "KeyPrefix";
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active, TimeSpan.FromHours(1));

            var server = PipelineFactory.CreateServer(o =>
            {
                _options(o);

                o.SaveToken = true;
                o.EnableCaching = true;
                o.CacheKeyPrefix = cacheKeyPrefix;
                o.CacheDuration = TimeSpan.FromMinutes(10);
            }, handler, true);
            var client = server.CreateClient();
            client.SetBearerToken(expectedToken);

            var firstResponse = await client.GetAsync("http://test");
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var secondResponse = await client.GetAsync("http://test");
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var responseDataStr = await secondResponse.Content.ReadAsStringAsync();
            var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseDataStr);

            responseData.Should().Contain("token", expectedToken);
            AssertCacheItemExists(server, cacheKeyPrefix, expectedToken);
        }

        [Fact]
        public async Task Repeated_active_token_with_caching_enabled_should_hit_cache()
        {
            var expectedToken = "expected_token";
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active, TimeSpan.FromHours(1));

            var server = PipelineFactory.CreateServer(o =>
            {
                _options(o);

                o.SaveToken = true;
                o.EnableCaching = true;
                o.CacheDuration = TimeSpan.FromMinutes(10);
            }, handler, true);
            var client = server.CreateClient();
            client.SetBearerToken(expectedToken);

            var firstResponse = await client.GetAsync("http://test");

            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            handler.SentIntrospectionRequest.Should().BeTrue();

            handler.SentIntrospectionRequest = false;
            var secondResponse = await client.GetAsync("http://test");
            handler.SentIntrospectionRequest.Should().BeFalse();
            AssertCacheItemExists(server, string.Empty, expectedToken);
        }

        [Fact]
        public async Task Repeated_inactive_token_with_caching_enabled_should_hit_cache()
        {
            var expectedToken = "expected_token";
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Inactive);

            var server = PipelineFactory.CreateServer(o =>
            {
                _options(o);

                o.SaveToken = true;
                o.EnableCaching = true;
                o.CacheDuration = TimeSpan.FromMinutes(10);
            }, handler, true);
            var client = server.CreateClient();
            client.SetBearerToken(expectedToken);

            var firstResponse = await client.GetAsync("http://test");

            firstResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            handler.SentIntrospectionRequest.Should().BeTrue();

            handler.SentIntrospectionRequest = false;
            var secondResponse = await client.GetAsync("http://test");
            secondResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            handler.SentIntrospectionRequest.Should().BeFalse();
            AssertCacheItemExists(server, string.Empty, expectedToken);
        }

        [Fact]
        public async Task ActiveToken_With_Discovery_Unavailable_On_First_Request()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);

            var client = PipelineFactory.CreateClient(o => _options(o), handler);
            client.SetBearerToken("sometoken");

            handler.IsDiscoveryFailureTest = true;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.GetAsync("http://test"));

            handler.IsDiscoveryFailureTest = false;
            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ActiveToken_RequestSending_AdditionalParameter_with_inline_event()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);

            var client = PipelineFactory.CreateClient(o =>
            {
                _options(o);

                o.Events.OnSendingRequest = e =>
                {
                    e.TokenIntrospectionRequest.Parameters = Parameters.FromObject(new { additionalParameter = "42" });
                    return Task.CompletedTask;
                };

            }, handler);

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);

            handler.LastRequest.Should().Contain(new KeyValuePair<string, string>("additionalParameter", "42"));
        }

        private void AssertCacheItemExists(TestServer testServer, string cacheKeyPrefix, string token)
        {
            var cache = testServer.Services.GetService<IDistributedCache>();
            var cacheItem = cache.GetString($"{cacheKeyPrefix}{token.ToSha256()}");

            cacheItem.Should().NotBeNullOrEmpty();
        }
    }
}