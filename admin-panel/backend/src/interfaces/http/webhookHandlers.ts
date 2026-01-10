import { getStripeSync } from '../../shared/infrastructure/stripe';
import { purchasesRepository } from '../../features/purchases/purchases.repository';
import { qrCodesRepository } from '../../features/inventory/qr-codes.repository';
import { usersRepository } from '../../features/users/users.repository';
import { notificationsRepository } from '../../features/notifications/notifications.repository';

export class WebhookHandlers {
  static async processWebhook(payload: Buffer, signature: string, uuid: string): Promise<void> {
    if (!Buffer.isBuffer(payload)) {
      throw new Error(
        'STRIPE WEBHOOK ERROR: Payload must be a Buffer. ' +
        'Received type: ' + typeof payload + '. ' +
        'FIX: Ensure webhook route is registered BEFORE app.use(express.json()).'
      );
    }

    const sync = await getStripeSync();
    await sync.processWebhook(payload, signature, uuid);
  }

  static async handleCheckoutComplete(session: any): Promise<void> {
    const purchaseId = session.metadata?.purchaseId;
    if (!purchaseId) {
      console.log('No purchaseId in session metadata');
      return;
    }

    try {
      const purchase = await purchasesRepository.getPurchase(parseInt(purchaseId));
      if (!purchase) {
        console.error('Purchase not found:', purchaseId);
        return;
      }

      const availableQr = await qrCodesRepository.findAvailableQrCode(
        purchase.stationId,
        purchase.fuelType,
        purchase.liters
      );

      if (availableQr) {
        await qrCodesRepository.assignQrToPurchase(purchase.id, availableQr.id);
        console.log(`Assigned QR ${availableQr.id} to purchase ${purchase.id}`);

        // Credit referral bonus if applicable
        const user = await usersRepository.getUser(purchase.sessionId);
        if (user && user.referredBy) {
          const referrer = await usersRepository.getUser(user.referredBy);
          if (referrer) {
            // 1% of purchase price or fixed amount (e.g. 10 UAH)
            const bonusAmount = 10;
            const newBalance = (referrer.bonusBalance || 0) + bonusAmount;
            await usersRepository.updateUser(referrer.id, { bonusBalance: newBalance });

            await notificationsRepository.createNotification({
              userId: referrer.id,
              title: "Referral Purchase Bonus!",
              message: `Your friend ${user.firstName || 'User'} made a purchase! You got ${bonusAmount} UAH.`,
            });
            console.log(`Credited referral bonus of ${bonusAmount} to ${referrer.id}`);
          }
        }

      } else {
        await purchasesRepository.updatePurchaseStatus(purchase.id, 'pending_qr');
        console.log(`No QR available for purchase ${purchase.id}, marked as pending_qr`);
      }
    } catch (error) {
      console.error('Error handling checkout complete:', error);
    }
  }
}
