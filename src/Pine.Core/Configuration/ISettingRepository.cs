using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Settings;

namespace Pine.Core.Configuration;

public interface ISettingRepository
{
    Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<Setting>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Setting setting, CancellationToken cancellationToken = default);
}
