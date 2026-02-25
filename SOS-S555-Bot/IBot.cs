using System;
using System.Threading.Tasks;

namespace SOSS555Bot
{
    public interface IBot
    {
        Task StartAsync(IServiceProvider services);

        Task StopAsync();
    }
}