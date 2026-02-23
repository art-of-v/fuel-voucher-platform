import "dotenv/config";
import { db } from "./src/shared/database/db";
import { users } from "./src/shared/database/schema";
import { eq } from "drizzle-orm";

async function check() {
    const allUsers = await db.select().from(users).orderBy(users.createdAt);
    allUsers.forEach(u => {
        console.log(`ID: ${u.id}, Phone: ${u.phone}, CreatedAt: ${u.createdAt}`);
    });
}

check().catch(console.error);
