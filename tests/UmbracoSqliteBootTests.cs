using Argentini.Umbraco.Search.Qdrant.Composers;
using Argentini.Umbraco.Search.Qdrant.Services;
using Argentini.Umbraco.Search.Qdrant.VectorStores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Umbraco.Cms.Core;
using Umbraco.AI.Search.Core.VectorStore;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Umbraco.Search.Qdrant.Tests;

[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class UmbracoSqliteBootTests : UmbracoIntegrationTest
{
    public UmbracoSqliteBootTests()
    {
        InMemoryConfiguration["Tests:Database:DatabaseType"] = "SQLite";
        InMemoryConfiguration["Tests:Database:PrepareThreadCount"] = "1";
        InMemoryConfiguration["Tests:Database:SchemaDatabaseCount"] = "1";
        InMemoryConfiguration["Tests:Database:EmptyDatabasesCount"] = "1";
    }

    [Test]
    public void BootsUmbracoWithSqlite()
    {
        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(ScopeProvider, Is.Not.Null);
            NUnit.Framework.Assert.That(ScopeAccessor, Is.Not.Null);
            NUnit.Framework.Assert.That(GetRequiredService<ITextReplacementProvider>(), Is.TypeOf<EmptyTextReplacementProvider>());
            NUnit.Framework.Assert.That(GetRequiredService<IAIVectorStore>(), Is.TypeOf<QdrantVectorStore>());
        });
    }

    [Test]
    public void CanPersistAndReadContentTypeFromSqlite()
    {
        var alias = CreateContentType();

        var saved = GetRequiredService<IContentTypeService>().Get(alias);

        NUnit.Framework.Assert.That(saved, Is.Not.Null);
        NUnit.Framework.Assert.That(saved!.Alias, Is.EqualTo(alias));
        NUnit.Framework.Assert.That(saved.AllowedAsRoot, Is.True);
    }

    [Test]
    public void CanPersistAndReadContentFromSqlite()
    {
        var alias = CreateContentType(includeBody: true);
        var contentService = GetRequiredService<IContentService>();

        var content = contentService.Create("Home", Constants.System.Root, alias);
        content.SetValue("body", "<p>Hello <strong>SQLite</strong></p>");
        contentService.Save(content);
        var saved = contentService.GetById(content.Key);

        NUnit.Framework.Assert.That(saved, Is.Not.Null);
        NUnit.Framework.Assert.That(saved!.Name, Is.EqualTo("Home"));
        NUnit.Framework.Assert.That(saved.ContentType.Alias, Is.EqualTo(alias));
        NUnit.Framework.Assert.That(saved.GetValue<string>("body"), Is.EqualTo("<p>Hello <strong>SQLite</strong></p>"));
    }

    private string CreateContentType(bool includeBody = false)
    {
        var contentTypeService = GetRequiredService<IContentTypeService>();
        var shortStringHelper = GetRequiredService<IShortStringHelper>();
        var alias = "article" + Guid.NewGuid().ToString("N")[..8];
        var contentType = new ContentType(shortStringHelper, Constants.System.Root)
        {
            Alias = alias,
            Name = "Article",
            AllowedAsRoot = true
        };

        if (includeBody)
        {
#pragma warning disable CS0618
            var dataType = GetRequiredService<IDataTypeService>().GetDataType(Constants.DataTypes.RichtextEditor);
#pragma warning restore CS0618
            NUnit.Framework.Assert.That(dataType, Is.Not.Null);
            var body = new PropertyType(shortStringHelper, dataType!)
            {
                Alias = "body",
                Name = "Body"
            };

            contentType.AddPropertyType(body);
        }

#pragma warning disable CS0618
        contentTypeService.Save(contentType);
#pragma warning restore CS0618

        return alias;
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        var qdrantInitializer = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(QdrantVectorStoreInitializer));

        if (qdrantInitializer is not null)
            services.Remove(qdrantInitializer);
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);

        new QdrantVectorStoreComposer().Compose(builder);
    }

    private new T GetRequiredService<T>()
        where T : notnull =>
        Services.GetRequiredService<T>();
}
