import { create } from 'zustand';
import { persist } from 'zustand/middleware';

export type Language = 'en' | 'uk' | 'de' | 'es';

export const languages: { code: Language; name: string; flag: string }[] = [
    { code: 'en', name: 'English', flag: '🇬🇧' },
    { code: 'uk', name: 'Українська', flag: '🇺🇦' },
    { code: 'de', name: 'Deutsch', flag: '🇩🇪' },
    { code: 'es', name: 'Español', flag: '🇪🇸' },
];

const translations: Record<Language, Record<string, string>> = {
    en: {
        // App
        'app.title': 'FUEL FLOW',
        'app.admin': 'ADMIN',

        // Sidebar Navigation
        'nav.management': 'Management',
        'nav.stations': 'Stations',
        'nav.fueltypes': 'Fuel Types',
        'nav.packages': 'Fuel Packages',
        'nav.purchases': 'Purchases',
        'nav.users': 'Users',
        'nav.qrcodes': 'QR Codes',
        'nav.vouchers': 'Import Vouchers',

        // User Profile
        'user.name': 'Admin System',
        'user.role': 'Super User',

        // Header
        'header.dashboard': 'Dashboard',
        'header.docs': 'Documentation',
        'header.support': 'Support',

        // Common
        'common.loading': 'Loading...',
        'common.save': 'Save',
        'common.cancel': 'Cancel',
        'common.delete': 'Delete',
        'common.deleteAll': 'Delete All',
        'common.edit': 'Edit',
        'common.create': 'Create',
        'common.search': 'Search...',
        'common.actions': 'Actions',
        'common.status': 'Status',
        'common.date': 'Date',

        // Vouchers / Import
        'import.title': 'Import Vouchers',
        'import.description': 'Upload PDF or Image files to import vouchers into the system.',
        'import.dragDrop': 'Drag & drop voucher images, PDFs, or ZIP here',
        'import.clickSelect': 'Click to select',
        'import.uploading': 'Uploading...',
        'import.processing': 'Processing...',
        'import.completed': 'Completed',
        'import.failed': 'Failed',
        'import.totalFiles': 'Total Files',
        'import.processed': 'Processed',
        'import.successful': 'Successful',
        'import.failedCount': 'Failed',

        // Voucher Table
        'vouchers.id': 'ID',
        'vouchers.image': 'Image',
        'vouchers.volume': 'Volume',
        'vouchers.fuelType': 'Fuel Type',
        'vouchers.provider': 'Provider',
        'vouchers.status': 'Status',
        'vouchers.importedAt': 'Imported At',
        'vouchers.expires': 'Expires',
        'vouchers.externalId': 'External ID',
    },

    uk: {
        // App
        'app.title': 'FUEL FLOW',
        'app.admin': 'АДМІН',

        // Sidebar Navigation
        'nav.management': 'Управління',
        'nav.stations': 'Станції',
        'nav.fueltypes': 'Типи пального',
        'nav.packages': 'Паливні пакети',
        'nav.purchases': 'Покупки',
        'nav.users': 'Користувачі',
        'nav.qrcodes': 'QR-коди',
        'nav.vouchers': 'Імпорт талонів',

        // User Profile
        'user.name': 'Адміністратор',
        'user.role': 'Суперкористувач',

        // Header
        'header.dashboard': 'Дашборд',
        'header.docs': 'Документація',
        'header.support': 'Підтримка',

        // Common
        'common.loading': 'Завантаження...',
        'common.save': 'Зберегти',
        'common.cancel': 'Скасувати',
        'common.delete': 'Видалити',
        'common.deleteAll': 'Видалити усі',
        'common.edit': 'Редагувати',
        'common.create': 'Створити',
        'common.search': 'Пошук...',
        'common.actions': 'Дії',
        'common.status': 'Статус',
        'common.date': 'Дата',

        // Vouchers / Import
        'import.title': 'Імпорт талонів',
        'import.description': 'Завантажте PDF або зображення для імпорту талонів у систему.',
        'import.dragDrop': 'Перетягніть сюди зображення талонів, PDF або ZIP',
        'import.clickSelect': 'Натисніть, щоб вибрати',
        'import.uploading': 'Завантаження...',
        'import.processing': 'Обробка...',
        'import.completed': 'Завершено',
        'import.failed': 'Помилка',
        'import.totalFiles': 'Всього файлів',
        'import.processed': 'Оброблено',
        'import.successful': 'Успішно',
        'import.failedCount': 'Помилок',

        // Voucher Table
        'vouchers.id': 'ID',
        'vouchers.image': 'Зображення',
        'vouchers.volume': 'Об\'єм',
        'vouchers.fuelType': 'Тип пального',
        'vouchers.provider': 'Провайдер',
        'vouchers.status': 'Статус',
        'vouchers.importedAt': 'Імпортовано',
        'vouchers.expires': 'Термін дії',
        'vouchers.externalId': 'Зовнішній ID',
    },

    de: {
        // App
        'app.title': 'FUEL FLOW',
        'app.admin': 'ADMIN',

        // Sidebar Navigation
        'nav.management': 'Verwaltung',
        'nav.stations': 'Stationen',
        'nav.fueltypes': 'Kraftstoffarten',
        'nav.packages': 'Kraftstoffpakete',
        'nav.purchases': 'Einkäufe',
        'nav.users': 'Benutzer',
        'nav.qrcodes': 'QR-Codes',
        'nav.vouchers': 'Gutscheine Importieren',

        // User Profile
        'user.name': 'Admin-System',
        'user.role': 'Superuser',

        // Header
        'header.dashboard': 'Dashboard',
        'header.docs': 'Dokumentation',
        'header.support': 'Support',

        // Common
        'common.loading': 'Laden...',
        'common.save': 'Speichern',
        'common.cancel': 'Abbrechen',
        'common.delete': 'Löschen',
        'common.deleteAll': 'Alle löschen',
        'common.edit': 'Bearbeiten',
        'common.create': 'Erstellen',
        'common.search': 'Suchen...',
        'common.actions': 'Aktionen',
        'common.status': 'Status',
        'common.date': 'Datum',

        // Vouchers / Import
        'import.title': 'Gutscheine Importieren',
        'import.description': 'Laden Sie PDF- oder Bilddateien hoch, um Gutscheine in das System zu importieren.',
        'import.dragDrop': 'Gutscheinbilder, PDFs oder ZIP hierher ziehen',
        'import.clickSelect': 'Klicken zum Auswählen',
        'import.uploading': 'Hochladen...',
        'import.processing': 'Verarbeiten...',
        'import.completed': 'Abgeschlossen',
        'import.failed': 'Fehlgeschlagen',
        'import.totalFiles': 'Gesamtdateien',
        'import.processed': 'Verarbeitet',
        'import.successful': 'Erfolgreich',
        'import.failedCount': 'Fehlgeschlagen',

        // Voucher Table
        'vouchers.id': 'ID',
        'vouchers.image': 'Bild',
        'vouchers.volume': 'Volumen',
        'vouchers.fuelType': 'Kraftstoffart',
        'vouchers.provider': 'Anbieter',
        'vouchers.status': 'Status',
        'vouchers.importedAt': 'Importiert am',
        'vouchers.expires': 'Ablaufdatum',
        'vouchers.externalId': 'Externe ID',
    },

    es: {
        // App
        'app.title': 'FUEL FLOW',
        'app.admin': 'ADMIN',

        // Sidebar Navigation
        'nav.management': 'Gestión',
        'nav.stations': 'Estaciones',
        'nav.fueltypes': 'Tipos de Combustible',
        'nav.packages': 'Paquetes de Combustible',
        'nav.purchases': 'Compras',
        'nav.users': 'Usuarios',
        'nav.qrcodes': 'Códigos QR',
        'nav.vouchers': 'Importar Cupones',

        // User Profile
        'user.name': 'Sistema Admin',
        'user.role': 'Superusuario',

        // Header
        'header.dashboard': 'Panel',
        'header.docs': 'Documentación',
        'header.support': 'Soporte',

        // Common
        'common.loading': 'Cargando...',
        'common.save': 'Guardar',
        'common.cancel': 'Cancelar',
        'common.delete': 'Eliminar',
        'common.deleteAll': 'Eliminar todo',
        'common.edit': 'Editar',
        'common.create': 'Crear',
        'common.search': 'Buscar...',
        'common.actions': 'Acciones',
        'common.status': 'Estado',
        'common.date': 'Fecha',

        // Vouchers / Import
        'import.title': 'Importar Cupones',
        'import.description': 'Sube archivos PDF o de imagen para importar cupones al sistema.',
        'import.dragDrop': 'Arrastra imágenes de cupones, PDF o ZIP aquí',
        'import.clickSelect': 'Haz clic para seleccionar',
        'import.uploading': 'Subiendo...',
        'import.processing': 'Procesando...',
        'import.completed': 'Completado',
        'import.failed': 'Fallido',
        'import.totalFiles': 'Archivos Totales',
        'import.processed': 'Procesados',
        'import.successful': 'Exitosos',
        'import.failedCount': 'Fallidos',

        // Voucher Table
        'vouchers.id': 'ID',
        'vouchers.image': 'Imagen',
        'vouchers.volume': 'Volumen',
        'vouchers.fuelType': 'Tipo de Combustible',
        'vouchers.provider': 'Proveedor',
        'vouchers.status': 'Estado',
        'vouchers.importedAt': 'Importado El',
        'vouchers.expires': 'Caduca',
        'vouchers.externalId': 'ID Externo',
    },
};

interface I18nStore {
    language: Language;
    setLanguage: (lang: Language) => void;
    t: (key: string) => string;
}

export const useI18n = create<I18nStore>()(
    persist(
        (set, get) => ({
            language: 'uk', // Default to Ukrainian
            setLanguage: (lang) => set({ language: lang }),
            t: (key: string) => {
                const lang = get().language;
                return translations[lang]?.[key] || translations['en']?.[key] || key;
            },
        }),
        {
            name: 'admin-lemberg-language', // Unique storage key for admin
        }
    )
);
