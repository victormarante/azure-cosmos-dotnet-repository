﻿// Copyright (c) IEvangelist. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.CosmosRepository.Options;
using Microsoft.Azure.CosmosRepository.Providers;
using Microsoft.Azure.CosmosRepositoryTests.Stubs;
using Microsoft.Azure.CosmosRepository.Validators;
using Microsoft.Azure.CosmosRepositoryTests.Validators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.CosmosRepositoryTests.Providers
{
    public class DefaultCosmosContainerProviderTests
    {
        readonly Mock<IOptions<RepositoryOptions>> _options = new ();
        readonly Mock<ICosmosItemConfigurationProvider> _itemConfigurationProvider = new();
        readonly Mock<IRepositoryOptionsValidator> _repositoryOptionsValidator = new();
        readonly Mock<CosmosClient> _cosmosClient = new();
        readonly Mock<Database> _database = new();
        readonly Mock<Container> _container = new();
        readonly Mock<DatabaseResponse> _databaseResponse = new();
        readonly Mock<ContainerResponse> _containerResponse = new();
        readonly RepositoryOptions _repositoryOptions = new();

        public DefaultCosmosContainerProviderTests()
        {
            _databaseResponse.Setup(o => o.Database).Returns(_database.Object);
            _containerResponse.Setup(o => o.Container).Returns(_container.Object);
            _options.Setup(o => o.Value).Returns(_repositoryOptions);
            _containerResponse.Setup(o => o.Container).Returns(_container.Object);
        }

        ICosmosClientProvider GetClientProvider() =>
            new TestCosmosClientProvider(_cosmosClient.Object);

        DefaultCosmosContainerProvider<TestItem> CreateDefaultCosmosContainerProvider() =>
            new(GetClientProvider(),
                _options.Object,
                _itemConfigurationProvider.Object,
                new NullLogger<DefaultCosmosContainerProvider<TestItem>>(),
                _repositoryOptionsValidator.Object);

        [Fact]
        public async Task GetContainerAsyncWhenContainerPerItemTypeIsNotSetGetsCorrectContainer()
        {
            //Arrange
            ICosmosContainerProvider<TestItem> provider = CreateDefaultCosmosContainerProvider();
            _repositoryOptions.ContainerPerItemType = false;
            _repositoryOptions.ContainerId = "containerA";

            ItemOptions itemOptions = new (typeof(TestItem), "a", "/id", new());

            _itemConfigurationProvider.Setup(o => o.GetOptions<TestItem>()).Returns(itemOptions);

            _cosmosClient.Setup(o =>
                    o.CreateDatabaseIfNotExistsAsync(_repositoryOptions.DatabaseId, (int?)null, null, CancellationToken.None))
                .ReturnsAsync(_databaseResponse.Object);

            _database.Setup(o =>
                o.CreateContainerIfNotExistsAsync(
                    It.Is<ContainerProperties>(c => c.Id == "containerA"
                                                    && c.PartitionKeyPath == "/id"),
                    (int?) null,
                    null,
                    CancellationToken.None))
                .ReturnsAsync(_containerResponse.Object);

            //Act
            Container container = await provider.GetContainerAsync();

            //Assert
            Assert.Equal(_container.Object, container);
            _container.Verify(o => o.ReplaceContainerAsync(It.IsAny<ContainerProperties>(), null, CancellationToken.None), Times.Never);
        }

        [Fact]
        public async Task GetContainerAsyncWhenContainerPerItemTypeIsSetGetsCorrectContainer()
        {
            //Arrange
            ICosmosContainerProvider<TestItem> provider = CreateDefaultCosmosContainerProvider();
            _repositoryOptions.ContainerPerItemType = true;

            ItemOptions itemOptions = new (typeof(TestItem), "a", "/test", new(), 5);

            _itemConfigurationProvider.Setup(o => o.GetOptions<TestItem>()).Returns(itemOptions);

            _cosmosClient.Setup(o =>
                    o.CreateDatabaseIfNotExistsAsync(_repositoryOptions.DatabaseId, (int?)null, null, CancellationToken.None))
                .ReturnsAsync(_databaseResponse.Object);

            _database.Setup(o =>
                    o.CreateContainerIfNotExistsAsync(
                        It.Is<ContainerProperties>(c => c.Id == "a"
                                                        && c.PartitionKeyPath == "/test"
                                                        && c.DefaultTimeToLive == 5),
                        (int?) null,
                        null,
                        CancellationToken.None))
                .ReturnsAsync(_containerResponse.Object);

            //Act
            Container container = await provider.GetContainerAsync();

            //Assert
            Assert.Equal(_container.Object, container);
            _container.Verify(o => o.ReplaceContainerAsync(It.IsAny<ContainerProperties>(), null, CancellationToken.None), Times.Never);
        }

        [Fact]
        public async Task GetContainerAsyncWhenSyncContainerPropertiesIsSetReplacesContainerProperties()
        {
            //Arrange
            ICosmosContainerProvider<TestItem> provider = CreateDefaultCosmosContainerProvider();
            _repositoryOptions.ContainerPerItemType = true;

            ItemOptions itemOptions = new (typeof(TestItem), "a", "/test", new(), 5, true);

            _itemConfigurationProvider.Setup(o => o.GetOptions<TestItem>()).Returns(itemOptions);

            _cosmosClient.Setup(o =>
                    o.CreateDatabaseIfNotExistsAsync(_repositoryOptions.DatabaseId, (int?)null, null, CancellationToken.None))
                .ReturnsAsync(_databaseResponse.Object);

            _database.Setup(o =>
                    o.CreateContainerIfNotExistsAsync(
                        It.Is<ContainerProperties>(c => ValidateContainerProperties(c)),
                        (int?) null,
                        null,
                        CancellationToken.None))
                .ReturnsAsync(_containerResponse.Object);

            //Act
            Container container = await provider.GetContainerAsync();

            //Assert
            _container.Verify(o => o.ReplaceContainerAsync(It.Is<ContainerProperties>(c => ValidateContainerProperties(c)), null, CancellationToken.None), Times.Once);

            Assert.Equal(_container.Object, container);
        }

        private static bool ValidateContainerProperties(ContainerProperties properties) =>
            properties.Id == "a"
            && properties.PartitionKeyPath == "/test"
            && properties.DefaultTimeToLive == 5;
    }

    internal class TestCosmosClientProvider : ICosmosClientProvider
    {
        private readonly CosmosClient _cosmosClient;

        public TestCosmosClientProvider(CosmosClient cosmosClient) =>
            _cosmosClient = cosmosClient;

        public Task<T> UseClientAsync<T>(Func<CosmosClient, Task<T>> consume)
            => consume(_cosmosClient) ?? throw new ArgumentNullException(nameof(consume));
    }
}