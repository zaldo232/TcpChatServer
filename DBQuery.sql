-- 1. DB ����
CREATE DATABASE ChatServerDb;
GO

-- 2. �ش� DB�� �̵�
USE ChatServerDb;
GO

-- 3. ChatMessages ���̺� ���� (������)
DROP TABLE IF EXISTS ChatMessages;
CREATE TABLE ChatMessages (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Sender NVARCHAR(100),
    Receiver NVARCHAR(100),
    Type NVARCHAR(20),
    Content NVARCHAR(MAX),
    FileName NVARCHAR(255),
    Timestamp DATETIME DEFAULT GETDATE(), -- �� ���� ��ǥ �����־���
    IsRead BIT NOT NULL DEFAULT 0,
    IsDeleted BIT NOT NULL DEFAULT 0
);

-- 4. Users ���̺� ����
DROP TABLE IF EXISTS Users;
CREATE TABLE Users (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Username NVARCHAR(50) UNIQUE NOT NULL,
    Password NVARCHAR(100) NOT NULL
);
