using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class StudentRecordRepository(AppDbContext dbContext) : IStudentRecordRepository
{
    public Task<StudentRecord?> GetByIdAsync(Guid studentRecordId, CancellationToken cancellationToken = default)
    {
        return dbContext.StudentRecords
            .FirstOrDefaultAsync(record => record.Id == studentRecordId, cancellationToken);
    }

    public async Task AddAsync(StudentRecord studentRecord, CancellationToken cancellationToken = default)
    {
        await dbContext.StudentRecords.AddAsync(studentRecord, cancellationToken);
    }
}
