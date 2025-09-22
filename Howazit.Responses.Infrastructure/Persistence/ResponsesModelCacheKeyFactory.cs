using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Howazit.Responses.Infrastructure.Persistence;

internal sealed class ResponsesModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => (context.GetType(),
            (context as ResponsesDbContext)?.EncryptUserAgent ?? false,
            designTime);
}