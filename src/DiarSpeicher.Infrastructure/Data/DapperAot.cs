using Dapper;
using DiarSpeicher.Core.Domain.Enums;
using static DiarSpeicher.Infrastructure.Data.SqliteTypeHandlers;

[module: DapperAot]
[module: TypeHandler(typeof(DateTimeOffset), typeof(DateTimeOffsetHandler))]
[module: TypeHandler(typeof(DateOnly), typeof(DateOnlyHandler))]
[module: TypeHandler(typeof(FileStatus), typeof(StringEnumTypeHandler<FileStatus>))]
[module: TypeHandler(typeof(LibraryPattern), typeof(StringEnumTypeHandler<LibraryPattern>))]
[module: TypeHandler(typeof(LibraryType), typeof(StringEnumTypeHandler<LibraryType>))]
[module: TypeHandler(typeof(ReadingStatus), typeof(StringEnumTypeHandler<ReadingStatus>))]
[module: TypeHandler(typeof(ReadingMode), typeof(StringEnumTypeHandler<ReadingMode>))]
[module: TypeHandler(typeof(ReadingDirection), typeof(StringEnumTypeHandler<ReadingDirection>))]
[module: TypeHandler(typeof(LibraryViewMode), typeof(StringEnumTypeHandler<LibraryViewMode>))]
