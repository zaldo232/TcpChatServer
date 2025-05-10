--  1. DB 생성
CREATE DATABASE ChatServerDb;
GO

--  2. 해당 DB로 이동
USE ChatServerDb;
GO

-- 3. 테이블 생성
CREATE TABLE ChatMessages (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Sender NVARCHAR(100),
    Receiver NVARCHAR(100),
    Type NVARCHAR(20),
    Content NVARCHAR(MAX),
    FileName NVARCHAR(255),
    Timestamp DATETIME DEFAULT GETDATE()
);
CREATE TABLE Users (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Username NVARCHAR(50) UNIQUE NOT NULL,
    Password NVARCHAR(100) NOT NULL
);

CREATE TABLE ChatRooms (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100)
);

CREATE TABLE RoomMembers (
    RoomId INT,
    Username NVARCHAR(100)
);

ALTER TABLE ChatMessages ADD IsRead BIT DEFAULT 0;
