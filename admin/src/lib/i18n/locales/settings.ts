import type { Language } from "../index";

export const settings: Record<Language, Record<string, string>> = {
  en: {
    "settings.autoRefund": "Auto-refund",
    "settings.enableAutoRefund": "Enable auto-refund",
    "settings.enableAutoRefundHint":
      "When enabled, partially fulfilled orders are automatically refunded once the grace period has passed.",
    "settings.gracePeriod": "Grace period (days)",
    "settings.gracePeriodHint":
      "7 = one week · 30 = one month · 365 = one year",
    "settings.save": "Save settings",
    "settings.saveChanges": "Save changes",
    "settings.saved": "Settings saved",
    "settings.title": "Settings",
    "settings.unsavedChanges": "Unsaved changes",
    "settings.saved2": "Saved",
    "settings.disabledNote":
      "Auto-refund is currently OFF. Orders stay partially fulfilled until you fulfil them or refund them manually.",
    "settings.qaTitle": "QA test access",
    "settings.qaWhat":
      "Lets the dedicated QA test account sign in to the production app for App Store review, using its reserved phone number and a server-side QA code. Turn it on only while QA is in progress and off immediately afterwards.",
    "settings.qaEnable": "Enable QA test access",
    "settings.qaEnableHint":
      "When on, the QA account can sign in with its QA code. When off, that sign-in stops working and any active QA session is signed out.",
    "settings.qaStatusEnabled": "Enabled",
    "settings.qaStatusDisabled": "Disabled",
    "settings.qaPhone": "QA account phone",
    "settings.qaNotConfigured":
      "No QA code is configured on the server, so QA sign-in cannot work even when enabled. Set the QA code secret in the deployment before enabling.",
    "settings.qaDisabledNote":
      "QA test access is OFF. The QA phone and code do not grant access.",
    "settings.qaLastChanged": "Last changed",
    "settings.qaSaved": "QA test access updated",
    "settings.deletedUnpaidOrderCleanupTitle": "Deleted unpaid order cleanup",
    "settings.deletedUnpaidOrderCleanupWhat":
      "Permanently deletes abandoned checkouts — orders a customer removed that were never paid and have since been cancelled — once they are older than the retention window. Frees database space and declutters the purchases view. Deletion cannot be undone.",
    "settings.enableDeletedUnpaidOrderCleanup":
      "Enable deletion of deleted unpaid orders",
    "settings.enableDeletedUnpaidOrderCleanupHint":
      "When enabled, a nightly job permanently deletes abandoned, cancelled orders older than the retention period.",
    "settings.retentionPeriod": "Retention period (days)",
    "settings.retentionPeriodHint":
      "7 = one week · 30 = one month · 365 = one year",
    "settings.deletedUnpaidOrderCleanupDisabledNote":
      "Order cleanup is currently OFF. Abandoned orders are kept indefinitely and never deleted.",
    "settings.deletedUnpaidOrderCleanupEnabledNote":
      "Order cleanup is ON. Abandoned, cancelled orders older than the retention period are permanently deleted every night — this cannot be undone.",
    "settings.dataRetentionTitle": "Data retention",
    "settings.dataRetentionWhat":
      "Permanently prunes rows in high-churn operational tables that have outlived their usefulness — spent one-time codes, dead login tokens, processed events, read notifications, old error logs and stale push tokens — each past its own fixed retention window. Live data is never touched, and orders are handled by order cleanup above. Deletion cannot be undone.",
    "settings.enableDataRetention": "Enable data retention",
    "settings.enableDataRetentionHint":
      "When enabled, a nightly job permanently deletes eligible rows. While off, it only reports how many rows would be removed and deletes nothing.",
    "settings.dataRetentionDisabledNote":
      "Data retention is currently OFF. The nightly job only counts eligible rows (dry-run) and deletes nothing.",
    "settings.dataRetentionEnabledNote":
      "Data retention is ON. Eligible rows past their retention window are permanently deleted every night — this cannot be undone. Valid tokens, unread notifications and unprocessed events are never affected.",
    "settings.expiredLossTitle": "Expired-stock loss",
    "settings.expiredLossWhat":
      "Books the cost of operator stock that lapsed unsold as a realised loss: vouchers you imported but never sold are retired to Expired once past their printed expiration date, so per-batch P&L stops counting them as sellable stock. Only your own unsold stock is affected — vouchers already sold to a customer are never touched.",
    "settings.enableExpiredLoss": "Enable expired-loss booking",
    "settings.enableExpiredLossHint":
      "When enabled, a nightly job retires lapsed unsold vouchers to Expired and books their cost as a loss. While off, it only logs the loss it would book and changes nothing.",
    "settings.expiredLossDisabledNote":
      "Expired-loss booking is currently OFF. The nightly job only logs the loss it would book (dry-run) and never changes voucher status.",
    "settings.expiredLossEnabledNote":
      "Expired-loss booking is ON. Unsold vouchers past their expiration date are retired to Expired every night and their cost is booked as a realised loss. Sold, customer-owned vouchers are never affected.",
    "appearance.title": "Appearance",
    "appearance.theme": "Color theme",
    "appearance.themeHint":
      "Applies to this browser only. Saved on this device.",
  },
  uk: {
    "settings.autoRefund": "Автоповернення",
    "settings.enableAutoRefund": "Увімкнути автоповернення",
    "settings.enableAutoRefundHint":
      "Після увімкнення частково виконані замовлення автоматично повертають кошти після закінчення пільгового періоду.",
    "settings.gracePeriod": "Пільговий період (дні)",
    "settings.gracePeriodHint":
      "7 = один тиждень · 30 = один місяць · 365 = один рік",
    "settings.save": "Зберегти налаштування",
    "settings.saveChanges": "Зберегти зміни",
    "settings.saved": "Налаштування збережено",
    "settings.title": "Налаштування",
    "settings.unsavedChanges": "Незбережені зміни",
    "settings.saved2": "Збережено",
    "settings.disabledNote":
      "Автоповернення ВИМКНЕНО. Замовлення залишаються частково виконаними, поки ви не виконаєте їх або не повернете кошти вручну.",
    "settings.qaTitle": "Тестовий доступ QA",
    "settings.qaWhat":
      "Дозволяє спеціальному тестовому акаунту QA входити у продакшн-застосунок для перевірки App Store, використовуючи зарезервований номер телефону та серверний QA-код. Вмикайте лише під час тестування QA і одразу вимикайте після нього.",
    "settings.qaEnable": "Увімкнути тестовий доступ QA",
    "settings.qaEnableHint":
      "Коли увімкнено, акаунт QA може входити за QA-кодом. Коли вимкнено, цей вхід перестає працювати, а активна сесія QA завершується.",
    "settings.qaStatusEnabled": "Увімкнено",
    "settings.qaStatusDisabled": "Вимкнено",
    "settings.qaPhone": "Телефон акаунта QA",
    "settings.qaNotConfigured":
      "На сервері не налаштовано QA-код, тож вхід QA не працюватиме навіть після увімкнення. Задайте секрет QA-коду в конфігурації розгортання перед увімкненням.",
    "settings.qaDisabledNote":
      "Тестовий доступ QA ВИМКНЕНО. Номер і код QA не надають доступу.",
    "settings.qaLastChanged": "Останню зміну зроблено",
    "settings.qaSaved": "Тестовий доступ QA оновлено",
    "settings.deletedUnpaidOrderCleanupTitle":
      "Видалення неоплачених замовлень",
    "settings.deletedUnpaidOrderCleanupWhat":
      "Остаточно видаляє покинуті оформлення — замовлення, які клієнт прибрав, так і не оплатив і які відтоді скасовано, — коли вони стають старшими за період зберігання. Звільняє місце в базі даних і розвантажує розділ покупок. Видалення не можна скасувати.",
    "settings.enableDeletedUnpaidOrderCleanup":
      "Увімкнути видалення неоплачених замовлень",
    "settings.enableDeletedUnpaidOrderCleanupHint":
      "Після увімкнення нічне завдання остаточно видаляє покинуті скасовані замовлення, старші за період зберігання.",
    "settings.retentionPeriod": "Період зберігання (днів)",
    "settings.retentionPeriodHint":
      "7 = один тиждень · 30 = один місяць · 365 = один рік",
    "settings.deletedUnpaidOrderCleanupDisabledNote":
      "Очищення замовлень ВИМКНЕНО. Покинуті замовлення зберігаються безстроково й ніколи не видаляються.",
    "settings.deletedUnpaidOrderCleanupEnabledNote":
      "Очищення замовлень УВІМКНЕНО. Покинуті скасовані замовлення, старші за період зберігання, остаточно видаляються щоночі — це не можна скасувати.",
    "settings.dataRetentionTitle": "Зберігання даних",
    "settings.dataRetentionWhat":
      "Остаточно видаляє рядки у високонавантажених службових таблицях, які вже не потрібні, — використані одноразові коди, недійсні токени входу, оброблені події, прочитані сповіщення, старі журнали помилок і застарілі push-токени, — кожен за власним фіксованим періодом зберігання. Активні дані не чіпаються, а замовлення обробляє очищення замовлень вище. Видалення не можна скасувати.",
    "settings.enableDataRetention": "Увімкнути зберігання даних",
    "settings.enableDataRetentionHint":
      "Після увімкнення нічне завдання остаточно видаляє прийнятні рядки. Поки вимкнено, воно лише повідомляє, скільки рядків було б видалено, і нічого не видаляє.",
    "settings.dataRetentionDisabledNote":
      "Зберігання даних ВИМКНЕНО. Нічне завдання лише підраховує прийнятні рядки (пробний запуск) і нічого не видаляє.",
    "settings.dataRetentionEnabledNote":
      "Зберігання даних УВІМКНЕНО. Прийнятні рядки, старші за період зберігання, остаточно видаляються щоночі — це не можна скасувати. Дійсні токени, непрочитані сповіщення та необроблені події ніколи не зачіпаються.",
    "settings.expiredLossTitle": "Збиток від протермінованого залишку",
    "settings.expiredLossWhat":
      "Списує вартість непроданого залишку оператора як реалізований збиток: талони, які ви імпортували, але так і не продали, після завершення надрукованого терміну дії переводяться в стан «Протерміновано», щоб P&L партії більше не рахував їх як придатний до продажу залишок. Зачіпається лише ваш власний непроданий залишок — талони, уже продані клієнту, ніколи не чіпаються.",
    "settings.enableExpiredLoss": "Увімкнути списання протермінованих",
    "settings.enableExpiredLossHint":
      "Після увімкнення нічне завдання переводить протерміновані непродані талони в стан «Протерміновано» і списує їхню вартість як збиток. Поки вимкнено, воно лише занотовує збиток, який було б списано, і нічого не змінює.",
    "settings.expiredLossDisabledNote":
      "Списання протермінованих ВИМКНЕНО. Нічне завдання лише занотовує збиток, який було б списано (пробний запуск), і ніколи не змінює стан талонів.",
    "settings.expiredLossEnabledNote":
      "Списання протермінованих УВІМКНЕНО. Непродані талони після завершення терміну дії щоночі переводяться в стан «Протерміновано», а їхня вартість списується як реалізований збиток. Продані талони, що належать клієнтам, ніколи не зачіпаються.",
    "appearance.title": "Оформлення",
    "appearance.theme": "Кольорова тема",
    "appearance.themeHint":
      "Застосовується лише в цьому браузері. Збережено на цьому пристрої.",
  },
  de: {
    "settings.autoRefund": "Automatische Erstattung",
    "settings.enableAutoRefund": "Automatische Erstattung aktivieren",
    "settings.enableAutoRefundHint":
      "Wenn aktiviert, werden teilweise erfüllte Bestellungen nach Ablauf der Wartefrist automatisch erstattet.",
    "settings.gracePeriod": "Wartefrist (Tage)",
    "settings.gracePeriodHint":
      "7 = eine Woche · 30 = ein Monat · 365 = ein Jahr",
    "settings.save": "Einstellungen speichern",
    "settings.saveChanges": "Änderungen speichern",
    "settings.saved": "Einstellungen gespeichert",
    "settings.title": "Einstellungen",
    "settings.unsavedChanges": "Nicht gespeicherte Änderungen",
    "settings.saved2": "Gespeichert",
    "settings.disabledNote":
      "Automatische Erstattung ist AUS. Bestellungen bleiben teilweise erfüllt, bis Sie sie erfüllen oder manuell erstatten.",
    "settings.qaTitle": "QA-Testzugang",
    "settings.qaWhat":
      "Ermöglicht dem speziellen QA-Testkonto die Anmeldung in der Produktions-App für die App-Store-Prüfung – mit seiner reservierten Telefonnummer und einem serverseitigen QA-Code. Nur während der QA aktivieren und danach sofort deaktivieren.",
    "settings.qaEnable": "QA-Testzugang aktivieren",
    "settings.qaEnableHint":
      "Wenn aktiviert, kann sich das QA-Konto mit seinem QA-Code anmelden. Wenn deaktiviert, funktioniert diese Anmeldung nicht mehr und eine aktive QA-Sitzung wird abgemeldet.",
    "settings.qaStatusEnabled": "Aktiviert",
    "settings.qaStatusDisabled": "Deaktiviert",
    "settings.qaPhone": "Telefon des QA-Kontos",
    "settings.qaNotConfigured":
      "Auf dem Server ist kein QA-Code konfiguriert, daher funktioniert die QA-Anmeldung auch bei Aktivierung nicht. Legen Sie das QA-Code-Geheimnis im Deployment fest, bevor Sie aktivieren.",
    "settings.qaDisabledNote":
      "QA-Testzugang ist AUS. Die QA-Telefonnummer und der Code gewähren keinen Zugang.",
    "settings.qaLastChanged": "Zuletzt geändert",
    "settings.qaSaved": "QA-Testzugang aktualisiert",
    "settings.deletedUnpaidOrderCleanupTitle":
      "Bereinigung gelöschter unbezahlter Bestellungen",
    "settings.deletedUnpaidOrderCleanupWhat":
      "Löscht abgebrochene Bestellvorgänge endgültig — Bestellungen, die eine Kundin oder ein Kunde entfernt hat, die nie bezahlt und seither storniert wurden —, sobald sie älter als die Aufbewahrungsfrist sind. Gibt Datenbankspeicher frei und entlastet die Käufeansicht. Das Löschen kann nicht rückgängig gemacht werden.",
    "settings.enableDeletedUnpaidOrderCleanup":
      "Löschung gelöschter unbezahlter Bestellungen aktivieren",
    "settings.enableDeletedUnpaidOrderCleanupHint":
      "Wenn aktiviert, löscht ein nächtlicher Job abgebrochene, stornierte Bestellungen, die älter als die Aufbewahrungsfrist sind, endgültig.",
    "settings.retentionPeriod": "Aufbewahrungsfrist (Tage)",
    "settings.retentionPeriodHint":
      "7 = eine Woche · 30 = ein Monat · 365 = ein Jahr",
    "settings.deletedUnpaidOrderCleanupDisabledNote":
      "Bestellbereinigung ist AUS. Abgebrochene Bestellungen werden unbegrenzt aufbewahrt und nie gelöscht.",
    "settings.deletedUnpaidOrderCleanupEnabledNote":
      "Bestellbereinigung ist AN. Abgebrochene, stornierte Bestellungen, die älter als die Aufbewahrungsfrist sind, werden jede Nacht endgültig gelöscht — dies kann nicht rückgängig gemacht werden.",
    "settings.dataRetentionTitle": "Datenaufbewahrung",
    "settings.dataRetentionWhat":
      "Löscht endgültig Zeilen in stark frequentierten Betriebstabellen, die nicht mehr benötigt werden — verbrauchte Einmalcodes, tote Login-Token, verarbeitete Ereignisse, gelesene Benachrichtigungen, alte Fehlerprotokolle und veraltete Push-Token —, jeweils nach ihrer eigenen festen Aufbewahrungsfrist. Aktive Daten werden nie angetastet, und Bestellungen übernimmt die Bestellbereinigung oben. Das Löschen kann nicht rückgängig gemacht werden.",
    "settings.enableDataRetention": "Datenaufbewahrung aktivieren",
    "settings.enableDataRetentionHint":
      "Wenn aktiviert, löscht ein nächtlicher Job infrage kommende Zeilen endgültig. Solange aus, meldet er nur, wie viele Zeilen entfernt würden, und löscht nichts.",
    "settings.dataRetentionDisabledNote":
      "Datenaufbewahrung ist AUS. Der nächtliche Job zählt nur infrage kommende Zeilen (Probelauf) und löscht nichts.",
    "settings.dataRetentionEnabledNote":
      "Datenaufbewahrung ist AN. Infrage kommende Zeilen, die älter als ihre Aufbewahrungsfrist sind, werden jede Nacht endgültig gelöscht — dies kann nicht rückgängig gemacht werden. Gültige Token, ungelesene Benachrichtigungen und unverarbeitete Ereignisse sind nie betroffen.",
    "settings.expiredLossTitle": "Verlust durch abgelaufenen Bestand",
    "settings.expiredLossWhat":
      "Bucht die Kosten unverkauften Betreiberbestands als realisierten Verlust: Gutscheine, die Sie importiert, aber nie verkauft haben, werden nach ihrem aufgedruckten Ablaufdatum auf Abgelaufen gesetzt, damit die Chargen-P&L sie nicht mehr als verkaufsfähigen Bestand zählt. Betroffen ist nur Ihr eigener unverkaufter Bestand — bereits an Kundschaft verkaufte Gutscheine werden nie angetastet.",
    "settings.enableExpiredLoss":
      "Verlustbuchung für abgelaufene Gutscheine aktivieren",
    "settings.enableExpiredLossHint":
      "Wenn aktiviert, setzt ein nächtlicher Job abgelaufene unverkaufte Gutscheine auf Abgelaufen und bucht ihre Kosten als Verlust. Solange aus, protokolliert er nur den Verlust, der gebucht würde, und ändert nichts.",
    "settings.expiredLossDisabledNote":
      "Die Verlustbuchung für abgelaufene Gutscheine ist AUS. Der nächtliche Job protokolliert nur den Verlust, der gebucht würde (Probelauf), und ändert nie den Gutscheinstatus.",
    "settings.expiredLossEnabledNote":
      "Die Verlustbuchung für abgelaufene Gutscheine ist AN. Unverkaufte Gutscheine nach ihrem Ablaufdatum werden jede Nacht auf Abgelaufen gesetzt und ihre Kosten als realisierter Verlust gebucht. Verkaufte, kundeneigene Gutscheine sind nie betroffen.",
    "appearance.title": "Darstellung",
    "appearance.theme": "Farbthema",
    "appearance.themeHint":
      "Gilt nur für diesen Browser. Auf diesem Gerät gespeichert.",
  },
  es: {
    "settings.autoRefund": "Reembolso automático",
    "settings.enableAutoRefund": "Activar reembolso automático",
    "settings.enableAutoRefundHint":
      "Cuando está activado, los pedidos parcialmente cumplidos se reembolsan automáticamente una vez transcurrido el periodo de gracia.",
    "settings.gracePeriod": "Periodo de gracia (días)",
    "settings.gracePeriodHint": "7 = una semana · 30 = un mes · 365 = un año",
    "settings.save": "Guardar configuración",
    "settings.saveChanges": "Guardar cambios",
    "settings.saved": "Configuración guardada",
    "settings.title": "Ajustes",
    "settings.unsavedChanges": "Cambios sin guardar",
    "settings.saved2": "Guardado",
    "settings.disabledNote":
      "El reembolso automático está DESACTIVADO. Los pedidos permanecen parcialmente cumplidos hasta que los cumplas o los reembolses manualmente.",
    "settings.qaTitle": "Acceso de prueba QA",
    "settings.qaWhat":
      "Permite que la cuenta de prueba QA inicie sesión en la app de producción para la revisión de App Store, usando su número reservado y un código QA del servidor. Actívalo solo mientras dure la QA y desactívalo justo después.",
    "settings.qaEnable": "Activar acceso de prueba QA",
    "settings.qaEnableHint":
      "Cuando está activado, la cuenta QA puede iniciar sesión con su código QA. Cuando está desactivado, ese inicio de sesión deja de funcionar y cualquier sesión QA activa se cierra.",
    "settings.qaStatusEnabled": "Activado",
    "settings.qaStatusDisabled": "Desactivado",
    "settings.qaPhone": "Teléfono de la cuenta QA",
    "settings.qaNotConfigured":
      "No hay un código QA configurado en el servidor, por lo que el inicio de sesión QA no funcionará ni siquiera activado. Configura el secreto del código QA en el despliegue antes de activar.",
    "settings.qaDisabledNote":
      "El acceso de prueba QA está DESACTIVADO. El teléfono y el código QA no otorgan acceso.",
    "settings.qaLastChanged": "Último cambio",
    "settings.qaSaved": "Acceso de prueba QA actualizado",
    "settings.deletedUnpaidOrderCleanupTitle":
      "Limpieza de pedidos no pagados eliminados",
    "settings.deletedUnpaidOrderCleanupWhat":
      "Elimina permanentemente los pagos abandonados — pedidos que un cliente quitó, que nunca se pagaron y que desde entonces se han cancelado — cuando superan el periodo de retención. Libera espacio en la base de datos y despeja la vista de compras. La eliminación no se puede deshacer.",
    "settings.enableDeletedUnpaidOrderCleanup":
      "Activar la limpieza de pedidos no pagados eliminados",
    "settings.enableDeletedUnpaidOrderCleanupHint":
      "Cuando está activada, una tarea nocturna elimina permanentemente los pedidos abandonados y cancelados que superan el periodo de retención.",
    "settings.retentionPeriod": "Periodo de retención (días)",
    "settings.retentionPeriodHint":
      "7 = una semana · 30 = un mes · 365 = un año",
    "settings.deletedUnpaidOrderCleanupDisabledNote":
      "La limpieza de pedidos está DESACTIVADA. Los pedidos abandonados se conservan indefinidamente y nunca se eliminan.",
    "settings.deletedUnpaidOrderCleanupEnabledNote":
      "La limpieza de pedidos está ACTIVADA. Los pedidos abandonados y cancelados que superan el periodo de retención se eliminan permanentemente cada noche; esto no se puede deshacer.",
    "settings.dataRetentionTitle": "Retención de datos",
    "settings.dataRetentionWhat":
      "Elimina permanentemente filas de tablas operativas de alta rotación que ya no son útiles — códigos de un solo uso usados, tokens de inicio de sesión muertos, eventos procesados, notificaciones leídas, registros de errores antiguos y tokens push obsoletos —, cada uno según su propio periodo de retención fijo. Los datos activos nunca se tocan, y los pedidos los gestiona la limpieza de pedidos de arriba. La eliminación no se puede deshacer.",
    "settings.enableDataRetention": "Activar retención de datos",
    "settings.enableDataRetentionHint":
      "Cuando está activada, una tarea nocturna elimina permanentemente las filas aptas. Mientras está desactivada, solo informa de cuántas filas se eliminarían y no elimina nada.",
    "settings.dataRetentionDisabledNote":
      "La retención de datos está DESACTIVADA. La tarea nocturna solo cuenta las filas aptas (simulación) y no elimina nada.",
    "settings.dataRetentionEnabledNote":
      "La retención de datos está ACTIVADA. Las filas aptas que superan su periodo de retención se eliminan permanentemente cada noche; esto no se puede deshacer. Los tokens válidos, las notificaciones no leídas y los eventos no procesados nunca se ven afectados.",
    "settings.expiredLossTitle": "Pérdida por stock vencido",
    "settings.expiredLossWhat":
      "Registra el coste del stock del operador que caducó sin venderse como una pérdida realizada: los vales que importaste pero nunca vendiste pasan a Vencido una vez superada su fecha de vencimiento impresa, para que el P&L por lote deje de contarlos como stock vendible. Solo se ve afectado tu propio stock sin vender; los vales ya vendidos a un cliente nunca se tocan.",
    "settings.enableExpiredLoss": "Activar registro de pérdida por vencimiento",
    "settings.enableExpiredLossHint":
      "Cuando está activado, una tarea nocturna pasa los vales sin vender vencidos a Vencido y registra su coste como pérdida. Mientras está desactivado, solo anota la pérdida que registraría y no cambia nada.",
    "settings.expiredLossDisabledNote":
      "El registro de pérdida por vencimiento está DESACTIVADO. La tarea nocturna solo anota la pérdida que registraría (simulación) y nunca cambia el estado de los vales.",
    "settings.expiredLossEnabledNote":
      "El registro de pérdida por vencimiento está ACTIVADO. Los vales sin vender vencidos pasan a Vencido cada noche y su coste se registra como pérdida realizada. Los vales vendidos, propiedad de clientes, nunca se ven afectados.",
    "appearance.title": "Apariencia",
    "appearance.theme": "Tema de color",
    "appearance.themeHint":
      "Se aplica solo a este navegador. Guardado en este dispositivo.",
  },
};
