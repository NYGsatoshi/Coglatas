using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface IStudentRecordRepository
{
    Task<StudentRecord?> GetByIdAsync(Guid studentRecordId, CancellationToken cancellationToken = default);
    Task AddAsync(StudentRecord studentRecord, CancellationToken cancellationToken = default);
}
