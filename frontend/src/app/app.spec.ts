import { fakeAsync, TestBed, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { AuditEvent, DatasetSchema } from './models';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render workspace title', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.eyebrow')?.textContent).toContain('Data Repository');
  });

  it('should add a detail row', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const schema: DatasetSchema = {
      key: 'FX_RATES',
      name: 'FX Rates',
      description: 'FX',
      headerFields: [
        { name: 'book', label: 'Book', type: 'String', isKey: false, required: true, allowedValues: [] }
      ],
      detailFields: [
        { name: 'currencyPair', label: 'Currency Pair', type: 'Select', isKey: true, required: true, allowedValues: ['EURUSD'], defaultValue: 'EURUSD' }
      ],
      permissions: {
        readRoles: ['admin'],
        writeRoles: ['admin'],
        signoffRoles: ['admin'],
        datasetAdminRoles: ['admin']
      }
    };

    app.schemas.set([schema]);
    app.userId.set('admin');
    app.roleInput.set('Read,Write');
    app.selectSchema('FX_RATES');

    const before = app.rowDrafts().length;
    app.addRow();

    expect(app.rowDrafts().length).toBe(before + 1);
    expect(app.detailGridRows().length).toBe(before + 1);
    expect(app.rowDrafts()[before]['currencyPair']).toBe('EURUSD');
  });

  it('should coerce typed default values when adding a detail row', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const schema: DatasetSchema = {
      key: 'FX_RATES',
      name: 'FX Rates',
      description: 'FX',
      headerFields: [],
      detailFields: [
        { name: 'isActive', label: 'Is Active', type: 'Boolean', isKey: false, required: false, allowedValues: [], defaultValue: 'true' },
        { name: 'priority', label: 'Priority', type: 'Number', isKey: false, required: false, allowedValues: [], defaultValue: '7' }
      ],
      permissions: {
        readRoles: ['admin'],
        writeRoles: ['admin'],
        signoffRoles: ['admin'],
        datasetAdminRoles: ['admin']
      }
    };

    app.schemas.set([schema]);
    app.userId.set('admin');
    app.roleInput.set('Read,Write');
    app.selectSchema('FX_RATES');

    const before = app.rowDrafts().length;
    app.addRow();

    expect(app.rowDrafts()[before]['isActive']).toBeTrue();
    expect(app.rowDrafts()[before]['priority']).toBe(7);
  });

  it('should add a new row when enter is pressed on the last detail field of the last row', fakeAsync(() => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const schema: DatasetSchema = {
      key: 'FX_RATES',
      name: 'FX Rates',
      description: 'FX',
      headerFields: [],
      detailFields: [
        { name: 'currencyPair', label: 'Currency Pair', type: 'String', isKey: true, required: true, allowedValues: [] },
        { name: 'rate', label: 'Rate', type: 'Number', isKey: false, required: true, allowedValues: [] }
      ],
      permissions: {
        readRoles: ['admin'],
        writeRoles: ['admin'],
        signoffRoles: ['admin'],
        datasetAdminRoles: ['admin']
      }
    };

    const editorInput = document.createElement('input');
    spyOn(document, 'querySelector').and.returnValue(editorInput);
    spyOn(editorInput, 'focus');
    spyOn(editorInput, 'select');

    const gridApi = {
      isDestroyed: () => false,
      applyTransaction: jasmine.createSpy('applyTransaction'),
      ensureIndexVisible: jasmine.createSpy('ensureIndexVisible'),
      refreshCells: jasmine.createSpy('refreshCells'),
      setFocusedCell: jasmine.createSpy('setFocusedCell'),
      startEditingCell: jasmine.createSpy('startEditingCell'),
      stopEditing: jasmine.createSpy('stopEditing')
    };

    app.schemas.set([schema]);
    app.selectedSchemaKey.set(schema.key);
    app.userId.set('admin');
    app.roleInput.set('Writer');
    (app as any).gridApi.set(gridApi);
    (app as any).setDetailRows([{ currencyPair: 'EURUSD', rate: 1.25 } as Record<string, unknown>]);

    const preventDefault = jasmine.createSpy('preventDefault');
    const stopPropagation = jasmine.createSpy('stopPropagation');

    app.onDetailGridCellKeyDown({
      event: { key: 'Enter', shiftKey: false, preventDefault, stopPropagation } as unknown as KeyboardEvent,
      colDef: { field: 'rate' },
      node: { rowIndex: 0 }
    } as any);

    tick();
    tick();

    expect(preventDefault).toHaveBeenCalled();
    expect(stopPropagation).toHaveBeenCalled();
    expect(gridApi.stopEditing).toHaveBeenCalled();
    expect(app.rowDrafts().length).toBe(2);
  }));

  it('should focus the first detail cell after add row', fakeAsync(() => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const schema: DatasetSchema = {
      key: 'FX_RATES',
      name: 'FX Rates',
      description: 'FX',
      headerFields: [],
      detailFields: [
        { name: 'currencyPair', label: 'Currency Pair', type: 'String', isKey: true, required: true, allowedValues: [] }
      ],
      permissions: {
        readRoles: ['admin'],
        writeRoles: ['admin'],
        signoffRoles: ['admin'],
        datasetAdminRoles: ['admin']
      }
    };

    const editorInput = document.createElement('input');
    editorInput.value = 'prefill';
    spyOn(document, 'querySelector').and.returnValue(editorInput);
    spyOn(editorInput, 'focus');
    spyOn(editorInput, 'setSelectionRange');
    spyOn(editorInput, 'select');

    const gridApi = {
      isDestroyed: () => false,
      applyTransaction: jasmine.createSpy('applyTransaction'),
      ensureIndexVisible: jasmine.createSpy('ensureIndexVisible'),
      refreshCells: jasmine.createSpy('refreshCells'),
      setFocusedCell: jasmine.createSpy('setFocusedCell'),
      startEditingCell: jasmine.createSpy('startEditingCell')
    };

    app.schemas.set([schema]);
    app.selectedSchemaKey.set(schema.key);
    app.userId.set('admin');
    app.roleInput.set('Writer');
    (app as any).gridApi.set(gridApi);
    (app as any).setDetailRows([] as Record<string, unknown>[]);

    app.addRow();
    tick();
    tick();

    expect(gridApi.applyTransaction).toHaveBeenCalled();
    expect(gridApi.setFocusedCell).toHaveBeenCalledWith(0, 'currencyPair');
    expect(gridApi.startEditingCell).toHaveBeenCalledWith({ rowIndex: 0, colKey: 'currencyPair' });
    expect(editorInput.focus).toHaveBeenCalled();
    expect(editorInput.setSelectionRange).toHaveBeenCalledWith(0, editorInput.value.length);
    expect(editorInput.select).toHaveBeenCalled();
  }));

  it('should default simulated user and roles', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;

    expect(app.userId()).toBe('Admin');
    expect(app.roleInput()).toBe('Reader,Writer,Approver,Admin,DatasetAdmin');
  });

  it('should create new schema template with mapped default roles', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance as unknown as {
      buildNewSchemaTemplate: (key: string, name: string) => DatasetSchema;
    };

    const schema = app.buildNewSchemaTemplate('FX_NEW', 'FxNew');

    expect(schema.permissions.readRoles).toEqual(['Reader']);
    expect(schema.permissions.writeRoles).toEqual(['Writer']);
    expect(schema.permissions.signoffRoles).toEqual(['Approver']);
    expect(schema.permissions.datasetAdminRoles).toEqual(['Admin']);
  });

  it('should convert dataset key to PascalCase name', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance as unknown as {
      toPascalCaseFromKey: (key: string) => string;
    };

    expect(app.toPascalCaseFromKey('FX_NEW')).toBe('FxNew');
    expect(app.toPascalCaseFromKey('DATASET_2026_TEST')).toBe('Dataset2026Test');
  });

  it('should parse Excel tab-delimited clipboard text', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance as unknown as {
      parseCsv: (text: string) => string[][];
    };

    const rows = app.parseCsv('sourcecurr\ttargetcurr\trate\nEUR\tUSD\t1.1525\nEUR\tJPY\t183.94');

    expect(rows).toEqual([
      ['sourcecurr', 'targetcurr', 'rate'],
      ['EUR', 'USD', '1.1525'],
      ['EUR', 'JPY', '183.94']
    ]);
  });

  it('should parse semicolon-delimited values', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance as unknown as {
      parseCsv: (text: string) => string[][];
    };

    const rows = app.parseCsv('a;b;c\n1;2;3');

    expect(rows).toEqual([
      ['a', 'b', 'c'],
      ['1', '2', '3']
    ]);
  });

  it('should summarize audit instance headers', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const event: AuditEvent = {
      id: '1',
      occurredAtUtc: '2026-04-12T00:00:00Z',
      userId: 'admin',
      action: 'INSTANCE_CREATE',
      datasetKey: 'FX_RATES',
      datasetInstanceId: 'instance-1',
      instanceHeader: {
        Book: 'LON',
        Desk: 'SPOT',
        Owner: 'Ops',
        Region: 'EMEA'
      },
      rowChanges: []
    };

    expect(app.getAuditInstanceSummary(event, 3)).toBe('Book: LON | Desk: SPOT | Owner: Ops | +1 more');
  });

  it('should focus and select the new schema field label after add', fakeAsync(() => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;

    const schema: DatasetSchema = {
      key: 'FX_RATES',
      name: 'FX Rates',
      description: 'FX',
      catalogueKey: undefined,
      headerFields: [],
      detailFields: [],
      permissions: {
        readRoles: ['admin'],
        writeRoles: ['admin'],
        signoffRoles: ['admin'],
        datasetAdminRoles: ['admin']
      }
    };

    app.schemas.set([schema]);
    app.selectedSchemaKey.set(schema.key);
    app.userId.set('admin');
    app.roleInput.set('DatasetAdmin');
    app.schemaBuilderDraft.set(schema);
    app.activeTab.set('schema');

    fixture.detectChanges();

    app.addSchemaField('detailFields');
    fixture.detectChanges();
    tick();
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('#schema-detailFields-label-0') as HTMLInputElement | null;
    expect(input).not.toBeNull();
    expect(document.activeElement).toBe(input);
    expect(input?.selectionStart).toBe(0);
    expect(input?.selectionEnd).toBe(input?.value.length);
  }));
});
