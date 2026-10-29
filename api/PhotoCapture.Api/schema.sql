use SSD_mobileCamera;

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