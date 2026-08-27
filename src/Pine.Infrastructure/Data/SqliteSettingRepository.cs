using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Configuration;
using Pine.Core.Settings;

namespace Pine.Infrastructure.Data;

/// <remarks>
/// PERSISTENCE IS NOT COMMITTED HERE (TD-01). This repository only stages changes on the scoped
/// <see cref="PineDbContext"/>; the write is flushed and committed by
/// <c>Pine.Core.Common.IUnitOfWork</c>, driven by the application layer's
/// <c>TransactionBehavior</c>. A repository that called <c>SaveChangesAsync</c> itself would make
/// every write its own implicit transaction, which is exactly the defect this class no longer has.
/// Any write path that does not go through a MediatR request marked <c>ITransactionalRequest</c>
/// will therefore never be persisted.
/// </remarks>
public class SqliteSettingRepository : ISettingRepository
{
    private readonly PineDbContext _context;

    public SqliteSettingRepository(PineDbContext context)
    {
        _context = context;
    }

    public async Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _context.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
    }

    public async Task<IEnumerable<Setting>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Settings.ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(Setting setting, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Settings.FirstOrDefaultAsync(s => s.Key == setting.Key, cancellationToken);
        if (existing == null)
        {
            _context.Settings.Add(setting);
        }
        else
        {
            existing.UpdateValue(setting.Value);
            _context.Settings.Update(existing);
        }
    }
}
