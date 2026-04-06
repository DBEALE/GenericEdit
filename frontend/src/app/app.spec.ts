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

  it('should render title', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Schema-driven data editing');
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
});
