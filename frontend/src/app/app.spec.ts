import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { DatasetSchema } from './models';

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
    expect(compiled.querySelector('.eyebrow')?.textContent).toContain('Dataset Operations Studio');
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
        { name: 'currencyPair', label: 'Currency Pair', type: 'Select', isKey: true, required: true, allowedValues: ['EURUSD'] }
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
  });

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
});
