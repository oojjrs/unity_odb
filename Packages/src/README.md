# UnityOdb

Unity용 단일 스레드 인덱스 메모리 데이터베이스다. `OdbContext`에서 모델을 선언하고 `OdbSet<TEntity, TKey>`로 추가·교체·삭제·조회를 수행한다.

## 모델 선언

```csharp
using System;
using oojjrs.odb;

public sealed class Item
{
    public string Code { get; set; }
    public int Id { get; set; }
    public int OwnerId { get; set; }
}

public sealed class GameDatabase : OdbContext
{
    public OdbSet<Item, int> Items => GetSet<Item, int>();

    protected override void OnModelCreating(OdbModelBuilder modelBuilder)
    {
        modelBuilder.SetSchemaVersion(1);
        modelBuilder.AddEntity<Item, int>("Item", item => item.Id)
            .SetCapacityHint(4096)
            .AddIndex(item => item.OwnerId)
            .AddUniqueIndex(item => item.Code, StringComparer.OrdinalIgnoreCase);
    }
}
```

- `TryFind`는 기본 키를 조회한다.
- `TryFindBy`는 unique index를 조회한다.
- `FindBy`는 non-unique index 결과를 allocation 없는 view로 반환한다.
- `Add`, `TryAdd`, `Replace`, `TryReplace`, `Remove`는 등록된 모든 인덱스를 함께 갱신한다.

## 비동기 스냅샷

```csharp
await database.InitializeAsync(source, new OdbXmlSnapshotImporter(), cancellationToken);
await database.ExportAsync(destination, new OdbXmlSnapshotExporter(), cancellationToken);
```

기본 구현은 UTF-8 XML과 JSON을 지원한다. `format`은 ODB 직렬화 형식의 식별자이고, `schemaVersion`은 `SetSchemaVersion`으로 선언한 Context 모델 버전이다. 기본 importer는 둘 다 확인하며 불일치하면 가져오기를 중단한다.

압축은 `GZipStream` 같은 표준 Stream wrapper로 조합한다. 저장 위치, Stream 해제, 재시도, 부분 내보내기는 호출자가 담당하며, 다른 포맷이나 선택 내보내기는 `OdbImporterInterface`와 `OdbExporterInterface`로 구현할 수 있다.

## 제약

- 단일 스레드 전용이며 내부 잠금과 트랜잭션이 없다.
- 데이터는 Context의 메모리에만 유지된다.
- `InitializeAsync`는 기존 데이터를 비운 뒤 가져오며 실패 시 rollback하지 않는다.
- 임의 LINQ 쿼리는 가능하지만 ODB 인덱스를 자동으로 사용하지 않는다.

전체 예제는 저장소의 `Assets/Sources/Scripts/Test.cs`부터 `Test4.cs`까지에서 확인할 수 있다.
