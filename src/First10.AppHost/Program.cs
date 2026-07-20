var builder = DistributedApplication.CreateBuilder(args);

var database = builder
    .AddPostgres("postgres")
    .WithDataVolume("first10-postgres-data")
    .AddDatabase("first10");

var objectStorageAccessKey = builder.AddParameter("object-storage-access-key", secret: true);
var objectStorageSecretKey = builder.AddParameter("object-storage-secret-key", secret: true);

var objectStorage = builder
    .AddContainer("object-storage", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", objectStorageAccessKey)
    .WithEnvironment("MINIO_ROOT_PASSWORD", objectStorageSecretKey)
    .WithHttpEndpoint(targetPort: 9000, name: "s3")
    .WithHttpEndpoint(targetPort: 9001, name: "console")
    .WithVolume("first10-object-storage-data", "/data");

var api = builder
    .AddProject<Projects.First10_Api>("api")
    .WithReference(database)
    .WithEnvironment("ObjectStorage__Endpoint", objectStorage.GetEndpoint("s3"))
    .WaitFor(database)
    .WaitFor(objectStorage);

builder
    .AddProject<Projects.First10_Worker>("worker")
    .WithReference(database)
    .WithEnvironment("ObjectStorage__Endpoint", objectStorage.GetEndpoint("s3"))
    .WaitFor(database)
    .WaitFor(objectStorage);

builder
    .AddViteApp("web", "../../web")
    .WithNpm(install: true)
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
