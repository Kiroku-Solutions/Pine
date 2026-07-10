using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Configuration;
using Pine.Core.Settings;

namespace Pine.Infrastructure.Data;

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
        await _context.SaveChangesAsync(cancellationToken);
    }
}
