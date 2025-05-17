-- 1. DB 생성
CREATE DATABASE ChatServerDb;
GO

-- 2. 해당 DB로 이동
USE ChatServerDb;
GO

-- 3. ChatMessages 테이블 생성 (수정됨)
DROP TABLE IF EXISTS ChatMessages;
CREATE TABLE ChatMessages (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Sender NVARCHAR(100),
    Receiver NVARCHAR(100),
    Type NVARCHAR(20),
    Content NVARCHAR(MAX),
    FileName NVARCHAR(255),
    Timestamp DATETIME DEFAULT GETDATE(), -- ← 여기 쉼표 빠져있었음
    IsRead BIT NOT NULL DEFAULT 0,
    IsDeleted BIT NOT NULL DEFAULT 0
);

-- 4. Users 테이블 생성
DROP TABLE IF EXISTS Users;
CREATE TABLE Users (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Username NVARCHAR(50) UNIQUE NOT NULL,
    Password NVARCHAR(100) NOT NULL
);
