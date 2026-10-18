-- AI Helper (package optimisation) storage.
--
--   aiAdviceLog          one row per advice the AI produced (request, model numbers, LLM status) plus the user's
--                        thumbs-up/down feedback. Feedback-rated advice is exported to judge prompt/provider changes.
--   aiMarketObservations real price observations harvested from the platform's own listings (and any manually
--                        added rows). The price model is periodically retrained on these, blended with the
--                        bootstrap data (tools/ai/train_price_model.py) - this is how the system learns from itself.
--
-- Idempotent: safe to re-run. Does not touch existing data. No USE statement on purpose - the app's connection
-- already targets the right database.

IF OBJECT_ID(N'dbo.aiAdviceLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.aiAdviceLog (
        adviceId        BIGINT IDENTITY(1,1) PRIMARY KEY,
        userId          INT            NOT NULL,
        roleCode        NVARCHAR(20)   NOT NULL,
        kind            NVARCHAR(30)   NULL,
        requestJson     NVARCHAR(MAX)  NOT NULL,
        responseJson    NVARCHAR(MAX)  NULL,
        userPriceUsd    DECIMAL(18,2)  NULL,
        p10Usd          DECIMAL(18,2)  NULL,
        p50Usd          DECIMAL(18,2)  NULL,
        p90Usd          DECIMAL(18,2)  NULL,
        modelVersion    NVARCHAR(40)   NULL,
        llmProvider     NVARCHAR(30)   NULL,
        llmModel        NVARCHAR(80)   NULL,
        narrativeStatus NVARCHAR(20)   NULL,
        latencyMs       INT            NULL,
        createdAt       DATETIME2      NOT NULL CONSTRAINT DF_aiAdviceLog_createdAt DEFAULT SYSUTCDATETIME(),
        feedback        SMALLINT       NULL,          -- +1 helpful, -1 not helpful
        feedbackComment NVARCHAR(1000) NULL,
        feedbackAt      DATETIME2      NULL
    );
    PRINT 'aiAdviceLog table created.';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_aiAdviceLog_user')
    CREATE INDEX IX_aiAdviceLog_user ON dbo.aiAdviceLog (userId, createdAt DESC);
GO

IF OBJECT_ID(N'dbo.aiMarketObservations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.aiMarketObservations (
        observationId BIGINT IDENTITY(1,1) PRIMARY KEY,
        kind          NVARCHAR(30)   NOT NULL,   -- PJ_PACKAGE | PH_RATE | VG_PACKAGE | FARM_DAILY_RATE | SPECIES_FEE
        rawJson       NVARCHAR(MAX)  NOT NULL,   -- the model input, same schema as tools/ai/common.py 'raw'
        targetUsd     DECIMAL(18,2)  NOT NULL,   -- observed price converted to USD
        weight        FLOAT          NOT NULL CONSTRAINT DF_aiMarketObservations_weight DEFAULT 1,
        source        NVARCHAR(40)   NOT NULL,   -- platform_listing | manual | ...
        sourceRef     NVARCHAR(80)   NOT NULL,   -- stable id of the originating row, keeps the harvest idempotent
        observedAt    DATETIME2      NOT NULL CONSTRAINT DF_aiMarketObservations_observedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_aiMarketObservations UNIQUE (kind, source, sourceRef)
    );
    PRINT 'aiMarketObservations table created.';
END;
GO
