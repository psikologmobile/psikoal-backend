using Microsoft.EntityFrameworkCore;
using Npgsql;
using PsikoAl.Common.Constants;
using PsikoAl.Common.Dtos;
using PsikoAl.Common.Exceptions;

namespace PsikoAl.Api.Middleware;

public sealed class DomainExceptionMiddleware(RequestDelegate next)
{
    private const string UniqueViolationSqlState = "23505";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException exception)
        {
            // `message` alanı eskiden hata KODUNUN kendisiydi. Mobil onu olduğu gibi
            // ekrana bastığı için kullanıcı şifre ekranında "AUTH_INVALID_CREDENTIALS",
            // ilan ekranında "LISTING_MAX_ACTIVE_EXCEEDED" görüyordu. Kod makineye,
            // mesaj kullanıcıya ait: `code` sözleşme gereği aynen korunuyor —
            // mobil onu dallanma için kullanıyor — `message` artık Türkçe.
            await WriteAsync(context, exception.ErrorKey, exception.Field, exception.Meta);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState } postgres)
        {
            await WriteAsync(context, UniqueViolationErrorKeys.For(postgres.ConstraintName), field: null, meta: null);
        }
    }

    private static Task WriteAsync(HttpContext context, string errorKey, string? field, object? meta)
    {
        context.Response.StatusCode = DomainErrorStatusCodes.For(errorKey);
        return context.Response.WriteAsJsonAsync(
            new ApiErrorDto(errorKey, ErrorMessagesTr.For(errorKey), field, meta),
            context.RequestAborted);
    }
}
