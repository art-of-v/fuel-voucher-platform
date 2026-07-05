
import express from 'express';
import session from 'express-session';
import { AppError } from '../../src/shared/errors/app-error';

export const createApp = (router: express.Router) => {
    const app = express();
    app.use(express.json());
    app.use(session({ secret: 'test', resave: false, saveUninitialized: true }));
    app.use('/', router);

    // Error handler
    app.use((err: any, req: any, res: any, next: any) => {
        const status = err.statusCode || 500;
        res.status(status).json({
            error: err.message,
            code: err.code
        });
    });
    return app;
};
