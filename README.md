# UnityOdb

Unity 프로세스 안에서 객체를 보관하고, 기본 키와 보조 인덱스로 빠르게 조회하는 단일 스레드 메모리 데이터베이스다.

## 설치

Unity 6000.0 이상에서 Package Manager의 `Install package from git URL...`에 다음 주소를 입력한다.

```text
https://github.com/oojjrs/unity_odb.git?path=/Packages/src
```

## 기본 사용법

`OdbContext`를 상속하고 사용할 엔터티, 기본 키, 인덱스를 한곳에서 선언한다. 별도의 인덱스 필드를 보관할 필요는 없다.

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

```csharp
var database = new GameDatabase();
database.Items.Add(new Item { Code = "potion", Id = 1, OwnerId = 10 });

database.Items.TryFind(1, out var item);
database.Items.TryFindBy(value => value.Code, "POTION", out var sameItem);
var ownerItems = database.Items.FindBy(value => value.OwnerId, 10);
```

## 가져오기와 내보내기

저장 위치와 스트림의 생명 주기는 호출자가 맡는다. 기본 XML·JSON 스냅샷은 UTF-8을 사용하고 모델의 `schemaVersion`을 검증한다.

```csharp
await database.InitializeAsync(source, new OdbJsonSnapshotImporter(), cancellationToken);
await database.ExportAsync(destination, new OdbJsonSnapshotExporter(), cancellationToken);
```

압축이 필요하면 ODB 전용 옵션 대신 표준 `GZipStream`을 조합한다. XML은 `OdbXmlSnapshotImporter`와 `OdbXmlSnapshotExporter`로 같은 방식으로 사용할 수 있다. 일부 데이터만 저장해야 하면 `OdbImporterInterface` 또는 `OdbExporterInterface`를 구현한다.

## 범위

- 프로세스 메모리 안에서 동작하며 영구 저장소를 직접 관리하지 않는다.
- 단일 스레드 사용을 전제로 하며 잠금과 트랜잭션을 제공하지 않는다.
- 초기화 실패 시 rollback하지 않으므로 실패를 복구해야 한다면 새 Context에 적재한 뒤 교체한다.
- 일반 LINQ는 그대로 사용할 수 있지만, 인덱스 조회는 `TryFind`, `TryFindBy`, `FindBy`를 사용해야 한다.

더 자세한 API 설명은 [패키지 문서](Packages/src/README.md), 구현 방향과 성능 기준은 [설계 문서](Design.html), 실행 예제는 [테스트 스크립트](Assets/Sources/Scripts)를 참고한다.
