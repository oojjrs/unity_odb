# UnityOdb

Unity 프로세스 안에서 객체를 기본 키와 보조 인덱스로 조회하는 단일 스레드 메모리 데이터베이스다.

## 설치

Unity 6000.0 이상에서 Package Manager의 `Install package from git URL...`에 다음 주소를 입력한다.

```text
https://github.com/oojjrs/unity_odb.git?path=/Packages/src
```

## 구성 요소

| 구성 요소 | 종류 | 용도 |
| --- | --- | --- |
| [`OdbContext`](Packages/src/Runtime/OdbContext.cs) | 추상 클래스 | 모델을 구성하고 데이터 집합, reset 초기화, 추가 가져오기와 선택 가능한 스냅샷 출력을 관리한다. |
| [`OdbSet<TEntity, TKey>`](Packages/src/Runtime/OdbSet.cs) | 컬렉션 | 엔터티 추가·교체·삭제와 기본 키·인덱스 조회를 제공한다. |
| [`OdbModelBuilder`](Packages/src/Runtime/OdbModelBuilder.cs) | 빌더 | 엔터티, 기본 키, 선택적 identity 생성, 보조 인덱스와 스키마 버전을 선언한다. |
| [`OdbIdentity<TKey>`](Packages/src/Runtime/OdbIdentity.cs) | 모델 핸들 | 여러 엔터티 타입이 하나의 Context 전역 identity 순서를 공유하게 한다. |
| [`OdbIdentityState`](Packages/src/Runtime/OdbIdentityState.cs) | 스냅샷 상태 | 사용자 정의 스냅샷이 identity high-water mark를 보존할 수 있게 한다. |
| `OdbXmlSnapshotImporter` / `OdbXmlSnapshotExporter` | 스냅샷 | 동일한 XML 형식으로 포함된 엔터티를 가져오고 전체 또는 선택한 엔터티를 내보낸다. |
| `OdbJsonSnapshotImporter` / `OdbJsonSnapshotExporter` | 스냅샷 | 동일한 JSON 형식으로 포함된 엔터티를 가져오고 전체 또는 선택한 엔터티를 내보낸다. |

## 최소 사용법

```csharp
public sealed class GameDatabase : OdbContext
{
    public OdbSet<Item, int> Items => GetSet<Item, int>();

    protected override void OnModelCreating(OdbModelBuilder modelBuilder)
    {
        modelBuilder.AddEntity<Item, int>("Item", item => item.Id)
            .AddIndex(item => item.OwnerId);
    }
}
```

## 범위

- 데이터는 프로세스 메모리에만 유지되며 내부 잠금과 트랜잭션을 제공하지 않는다.
- `InitializeAsync`는 모든 Set을 비운 뒤 입력에 포함된 행을 추가하고, `ImportAsync`는 기존 Set을 비우지 않고 입력에 포함된 행을 추가한다.
- 기본 `ExportAsync`는 모든 엔터티를 기록하고 엔터티 타입을 전달한 overload는 선택한 엔터티만 같은 형식으로 기록한다.
- 가져오기 중 기본 키나 unique index가 중복되면 실패한다.
- 가져오기는 rollback하지 않으므로 입력 처리 중 실패하면 앞서 추가된 행이 남을 수 있다.

전체 API는 [패키지 문서](Packages/src/README.md), 구현 방향은 [설계 문서](Design.html), 실행 예제는 [테스트 스크립트](Assets/Sources/Scripts)를 참고한다.
