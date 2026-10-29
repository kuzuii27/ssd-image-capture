use SSD_mobileCamera;

-- Reference only: this file is never executed. desktop/PhotoCapture.Host/Program.cs owns the live migration.

CREATE TABLE Photos (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Name nvarchar(200) NOT NULL,
    Latitude float NOT NULL,
    Longitude float NOT NULL,
    Accuracy float NOT NULL,
    CapturedAt datetimeoffset NOT NULL,
    CaptureDate date NOT NULL,
    CaptureTime time NOT NULL,
    ReceivedAt datetimeoffset NOT NULL,
    ImagePath nvarchar(500) NOT NULL,
    Client nvarchar(200) NULL,
    Note nvarchar(2000) NULL,
    Address nvarchar(500) NULL,
    Activity nvarchar(50) NULL,
    UpdatedAt datetimeoffset NULL
);
CREATE INDEX IX_Photos_CaptureDate ON Photos(CaptureDate);

CREATE TABLE Tasks (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    TechnicianName nvarchar(200) NOT NULL,
    Status nvarchar(20) NOT NULL,
    Client nvarchar(200) NULL,
    JobType nvarchar(50) NULL,
    OldImei nvarchar(50) NULL,
    NewImei nvarchar(50) NULL,
    OldSim nvarchar(50) NULL,
    NewSim nvarchar(50) NULL,
    PlateNumber nvarchar(20) NULL,
    Note nvarchar(2000) NULL,
    TimeInLatitude float NULL,
    TimeInLongitude float NULL,
    TimeInAccuracy float NULL,
    TimeInAddress nvarchar(500) NULL,
    TimeInCapturedAt datetimeoffset NULL,
    TimeInDate date NULL,
    TimeInTime time NULL,
    ClientLatitude float NULL,
    ClientLongitude float NULL,
    ClientAccuracy float NULL,
    ClientAddress nvarchar(500) NULL,
    ClientCapturedAt datetimeoffset NULL,
    ClientDate date NULL,
    ClientTime time NULL,
    TimeOutLatitude float NULL,
    TimeOutLongitude float NULL,
    TimeOutAccuracy float NULL,
    TimeOutAddress nvarchar(500) NULL,
    TimeOutCapturedAt datetimeoffset NULL,
    TimeOutDate date NULL,
    TimeOutTime time NULL,
    ReviewStatus nvarchar(20) NOT NULL CONSTRAINT DF_Tasks_ReviewStatus DEFAULT 'none',
    ReviewReason nvarchar(500) NULL,
    ReviewedAt datetimeoffset NULL,
    ReviewedBy nvarchar(200) NULL,
    ReceivedAt datetimeoffset NOT NULL,
    UpdatedAt datetimeoffset NOT NULL
);
CREATE INDEX IX_Tasks_TimeInDate ON Tasks(TimeInDate);
CREATE INDEX IX_Tasks_Status ON Tasks(Status);
CREATE INDEX IX_Tasks_ReviewStatus ON Tasks(ReviewStatus);
CREATE INDEX IX_Tasks_TechnicianName ON Tasks(TechnicianName);

CREATE TABLE TaskPhotos (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    TaskId uniqueidentifier NOT NULL CONSTRAINT FK_TaskPhotos_Tasks FOREIGN KEY REFERENCES Tasks(Id) ON DELETE CASCADE,
    Kind nvarchar(20) NOT NULL,
    ImagePath nvarchar(500) NOT NULL,
    CapturedAt datetimeoffset NOT NULL,
    ReceivedAt datetimeoffset NOT NULL
);
CREATE INDEX IX_TaskPhotos_TaskId ON TaskPhotos(TaskId);

CREATE TABLE TaskReviews (
    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TaskId uniqueidentifier NOT NULL CONSTRAINT FK_TaskReviews_Tasks FOREIGN KEY REFERENCES Tasks(Id) ON DELETE CASCADE,
    Decision nvarchar(20) NOT NULL,
    Reason nvarchar(500) NULL,
    ReviewedBy nvarchar(200) NOT NULL,
    ReviewedAt datetimeoffset NOT NULL
);
CREATE INDEX IX_TaskReviews_TaskId ON TaskReviews(TaskId);

CREATE TABLE TaskEdits (
    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TaskId uniqueidentifier NOT NULL CONSTRAINT FK_TaskEdits_Tasks FOREIGN KEY REFERENCES Tasks(Id) ON DELETE CASCADE,
    Field nvarchar(50) NOT NULL,
    OldValue nvarchar(2000) NULL,
    NewValue nvarchar(2000) NULL,
    EditedBy nvarchar(200) NOT NULL,
    EditedAt datetimeoffset NOT NULL
);
CREATE INDEX IX_TaskEdits_TaskId ON TaskEdits(TaskId);
