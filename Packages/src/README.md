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

## 시나리오별 사용법

아래 예제는 선택에 필요한 호출만 보여주며 엔터티 타입과 Context 속성 선언은 생략한다.

### 기본 키 전략 고르기

#### 외부에서 받은 PK를 그대로 쓸 때

생성 설정 없이 등록하고, 추가 전에 PK를 지정한다. 문자열 키나 이미 서버에서 발급된 숫자 키도 이 방식으로 사용한다.

```csharp
// OnModelCreating
modelBuilder.AddEntity<Item, int>("Item", item => item.Id);

// Runtime
database.Items.Add(new Item { Id = 1001, Code = "potion" });
```

#### 엔터티 타입마다 독립 번호를 발급할 때

`int` 또는 `long` 키에 setter를 등록한다. `Add`와 `TryAdd`에는 키가 `0`인 새 엔터티를 전달한다.

```csharp
// OnModelCreating
modelBuilder.AddEntity<Item, int>("Item", item => item.Id).ValueGeneratedOnAdd((item, id) => item.Id = id);

// Runtime
var item = new Item { Code = "potion" };
database.Items.Add(item);
```

#### 여러 엔터티 타입이 한 번호열을 공유할 때

한 `OdbContext` 인스턴스 안에서 같은 타입의 PK를 공유하려면 `AddGlobalIdentity`의 반환값을 참여 엔터티 모두에 전달한다. 아래 번호는 새 Context에서 순서대로 추가한 경우다.

```csharp
// OnModelCreating
var globalIds = modelBuilder.AddGlobalIdentity<long>("GlobalId");
modelBuilder.AddEntity<GlobalItem, long>("GlobalItem", item => item.Id).ValueGeneratedOnAdd(globalIds, (item, id) => item.Id = id);
modelBuilder.AddEntity<GlobalOwner, long>("GlobalOwner", owner => owner.Id).ValueGeneratedOnAdd(globalIds, (owner, id) => owner.Id = id);

// Runtime
database.GlobalItems.Add(new GlobalItem { Code = "potion" });     // Id 1
database.GlobalOwners.Add(new GlobalOwner { Name = "player" });   // Id 2
```

#### setter가 없는 불변 엔터티일 때

모델에는 setter 없이 `ValueGeneratedOnAdd()`를 선언하고, 추가할 때 생성된 키를 factory로 받는다.

```csharp
// OnModelCreating
modelBuilder.AddEntity<ImmutableItem, long>("ImmutableItem", item => item.Id).ValueGeneratedOnAdd();

// Runtime
var item = database.ImmutableItems.AddGenerated(id => new ImmutableItem(id, "potion"));
```

충돌을 정상 분기로 처리하려면 `TryAddGenerated(factory, out entity)`를 사용한다.

자동 생성 키는 1부터 증가한다. 삭제, unique 충돌 또는 factory 예외로 소비된 번호는 다시 발급하지 않는다. setter 방식의 `TryAdd`가 충돌하면 setter를 `0`으로 다시 호출하므로 setter는 생성값과 기본값을 모두 받아야 한다.

### 데이터 작업 고르기

| 하려는 작업 | 모델 설정 | 런타임 호출 |
| --- | --- | --- |
| PK로 한 건 조회 | `AddEntity(...)` | `TryFind(primaryKey, out entity)` |
| 속성으로 한 건 조회 | `AddUniqueIndex(selector)` | `TryFindBy(selector, indexKey, out entity)` |
| 속성으로 여러 건 조회 | `AddIndex(selector)` | `FindBy(selector, indexKey)` |
| 충돌을 예외로 처리하며 추가·교체 | 없음 | `Add(entity)` / `Replace(primaryKey, replacement)` |
| 충돌을 정상 분기로 처리하며 추가·교체 | 없음 | `TryAdd(entity)` / `TryReplace(primaryKey, replacement)` |
| 삭제 | 없음 | `Remove(primaryKey)` |
| 인덱스 정합성 진단 | 없음 | `ValidateIndexes()` |

`FindBy`는 non-unique index 결과를 allocation 없는 view로 반환한다. 추가·교체·삭제는 등록된 모든 인덱스를 함께 갱신한다.

### 스냅샷 작업 고르기

| 하려는 작업 | 호출 | 결과 |
| --- | --- | --- |
| 현재 데이터를 버리고 스냅샷으로 초기화 | `InitializeAsync(source, importer, cancellationToken)` | 모든 Set과 identity state를 reset한 뒤 파일의 행을 추가 |
| 현재 데이터에 스냅샷을 병합 | `ImportAsync(source, importer, cancellationToken)` | 기존 Set과 identity state를 유지하며 파일의 행을 추가 |
| 모든 엔터티 내보내기 | `ExportAsync(destination, exporter, cancellationToken)` | 모델 전체를 기록 |
| 일부 엔터티만 내보내기 | `ExportAsync(destination, exporter, cancellationToken, typeof(Item))` | 지정한 엔터티와 관련 identity state만 기록 |

XML은 `OdbXmlSnapshotImporter` / `OdbXmlSnapshotExporter`, JSON은 `OdbJsonSnapshotImporter` / `OdbJsonSnapshotExporter`를 전달한다.

사용자 정의 포맷을 구현할 때 exporter는 `OdbExportSession.EntityTypes`, `GetEntities<TEntity>()`, `IdentityStates`를 기록한다. importer는 각 행을 `OdbImportSession.TryAdd(entity)`로 추가하고, identity state를 `AdvanceIdentityState(scope, name, keyTypeName, highWaterMark)`로 복원한다.

## 스냅샷 형식

기본 구현은 UTF-8 XML과 JSON을 지원한다. 파일에 등록된 엔터티가 전부 들어 있든 일부만 들어 있든 형식은 같고, importer는 파일에 실제로 포함된 엔터티만 처리한다.

내보내는 범위에 identity가 없으면 기존 `unity-odb-json-1`·`unity-odb-xml-1` 형식을 유지한다. identity가 포함되면 `-2` 형식으로 generator high-water mark를 함께 기록하며, importer는 `-1` 행의 최대 키로도 generator를 reseed한다.

예를 들어 identity를 설정하지 않은 `Item`만 포함하는 JSON 스냅샷은 다음과 같다.

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
- 가져오기 중 기본 키나 unique index가 중복되면 실패한다.
- 가져오기는 rollback하지 않으므로 입력 순서대로 처리하다 실패하면 앞서 추가된 행이나 반영된 identity state가 남을 수 있다.
- 임의 LINQ 쿼리는 가능하지만 ODB 인덱스를 자동으로 사용하지 않는다.

전체 예제는 저장소의 `Assets/Sources/Scripts/Test.cs`부터 `Test6.cs`까지에서 확인할 수 있다. `Test5.cs`는 같은 JSON/XML 형식으로 엔터티별 export, reset 초기화와 add 가져오기를 검증하고, `Test6.cs`는 호출자 지정 키와 엔터티별·Context 전역 identity 및 스냅샷 reseed를 검증한다.
