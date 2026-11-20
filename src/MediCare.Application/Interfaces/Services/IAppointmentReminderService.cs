
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Application.Interfaces.Services;

public interface IAppointmentReminderService
{
    Task ProcessDueRemindersAsync(
        CancellationToken cancellationToken = default);
}
