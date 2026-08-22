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
- `Try...` 계열은 조회 실패와 키 충돌처럼 예상된 실패를 `false`로 반환하며, 잘못된 인자·인덱스 사용이나 key selector/comparer에서 발생한 예외는 숨기지 않는다.

## 비동기 스냅샷

```csharp
await database.InitializeAsync(itemSource, new OdbXmlSnapshotImporter(), cancellationToken);
await database.ImportAsync(ownerSource, new OdbXmlSnapshotImporter(), cancellationToken);

await database.ExportAsync(allDestination, new OdbXmlSnapshotExporter(), cancellationToken);

var entityTypes = new[] { typeof(Item) };
await database.ExportAsync(itemDestination, new OdbXmlSnapshotExporter(), cancellationToken, entityTypes);
```

기본 구현은 UTF-8 XML과 JSON을 지원한다. 파일에 등록된 엔터티가 전부 들어 있든 일부만 들어 있든 형식은 같다. importer는 파일에 실제로 포함된 엔터티만 처리한다.

- `InitializeAsync`는 모든 Set을 비운 뒤 파일에 포함된 행을 추가한다. 파일에 없는 엔터티의 Set은 빈 상태가 된다.
- `ImportAsync`는 Set을 비우지 않고 파일에 포함된 행을 추가한다. 파일에 없는 엔터티의 Set은 그대로 유지된다.
- 기본 `ExportAsync`는 모든 엔터티를 기록한다. `entityTypes`를 전달하는 overload는 선택한 엔터티만 같은 형식으로 기록한다.
- 사용자 정의 exporter는 `OdbExportSession.EntityTypes`에서 선택 범위를 확인하고 해당 엔터티만 가져올 수 있다.

예를 들어 `Item`만 포함하는 JSON 스냅샷은 다음과 같다.

```json
{
  "format": "unity-odb-json-1",
  "schemaVersion": 1,
  "entities": [
    {
      "name": "Item",
      "rows": [
        { "Code": "potion", "Id": 1, "OwnerId": 10 }
      ]
    }
  ]
}
```

같은 범위의 XML 스냅샷은 다음과 같다.

```xml
<odbSnapshot format="unity-odb-xml-1" schemaVersion="1">
  <entity name="Item">
    <row>
      <Code>potion</Code>
      <Id>1</Id>
      <OwnerId>10</OwnerId>
    </row>
  </entity>
</odbSnapshot>
```

`name`은 모델에 등록된 엔터티 이름이고, `schemaVersion`은 `SetSchemaVersion`으로 선언한 Context 모델 버전이다. importer는 형식과 버전, 엔터티 이름을 확인하며 불일치하면 가져오기를 중단한다.

압축은 `GZipStream` 같은 표준 Stream wrapper로 조합한다. 저장 위치, Stream 해제와 재시도는 호출자가 담당하며, 다른 포맷은 `OdbImporterInterface`와 `OdbExporterInterface`로 구현할 수 있다.

## 제약

- 단일 스레드 전용이며 내부 잠금과 트랜잭션이 없다.
- 데이터는 Context의 메모리에만 유지된다.
- `InitializeAsync`는 reset 후 add, `ImportAsync`는 reset 없는 add로 동작한다.
- 가져오기 중 기본 키나 unique index가 중복되면 실패한다.
- 가져오기는 rollback하지 않으므로 입력 순서대로 처리하다 실패하면 앞서 추가된 행이 남을 수 있다.
- 임의 LINQ 쿼리는 가능하지만 ODB 인덱스를 자동으로 사용하지 않는다.

전체 예제는 저장소의 `Assets/Sources/Scripts/Test.cs`부터 `Test5.cs`까지에서 확인할 수 있다. `Test5.cs`는 같은 JSON/XML 형식으로 엔터티별 export, reset 초기화와 add 가져오기를 검증한다.
