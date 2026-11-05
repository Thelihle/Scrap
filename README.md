The Operator will have these functionalities:

1. Log in using clock number and password
2. Once successful, They see Index page with 2 options whether update details / scan part
3. UpdateDetails link will take them to Setup.cshtml to update Locations
4. ScanPart link will take user to ScanPart page
5. If scanned part number is valid (Exists in SysproCompanyB DB, InvMaster TBL, StockCode Column,
6. Then enter scrapEntry on scrapEntry page
7. Continue this process for new part number that needs to be scrapped.
8. View scrapHistory once submitted.

The team leader will have these functionalities:
1. Log in using clock number and password
2. Once successful, they should see Index view with dashboard
3. 1st Approver of the scrap data loaded for specific Location.
4. Update operator password, 
5. Update Their own location
6. Approve/reject new user operator access request on their location and give them tempo password
7. See pending/submitted scrap and Approve/Reject scrap. When rejecting, that's only when you add a reason.
8. All New user 1st time login should force user to put tempo password, and new password they are creating for themselves.
9. TL approves as per location(Department, Line, Shift)
10. Should be able to generate a report via excel/presentation/pdf

The Group Leader will have these functionalities:

1. Will login using the username (name.surname) and password.
2. They should see dashboard of all the scrap that has been approved / rejected with the TeamLeader who approved or rejected.
3. They should be able to approve/reject. When rejecting, that's only when you add a reason.
4. They should be able to see TeamLeaders who have requested to be added onto the system and approve/reject. If new teamleader is approved, then gives them credentials.
5. Manage TeamLeader password retrieval.
6. All New user 1st time login should force user to put tempo password, and new password they are creating for themselves.
7. GL approves as per department and shift since they can manage more than 1 lines
8. When they register, they should specify Department, Line(s), Shift
9. Should be able to generate a report via excel/presentation/pdf

The Manager will have these functionalities:

1. Will login using the username (name.surname) and password.
2. They should see dashboard of all the scrap that has been approved / rejected with the TeamLeader, GroupLeader who approved or rejected.
3. They should be able to approve/reject with a reason. When rejecting, that's only when you add a reason.
4. They should be able to see Group Leaders who have requested to be added onto the system and approve/reject. If new Group leader is approved, then gives them credentials.
5. All New user 1st time login should force user to put tempo password, and new password they are creating for themselves.
6. Manager approves as per department(s), Line(s) and shift(s) since they can manage more than 1 GL in different location
7. When they register, they should specify Department(s), Line(s), Shift(s)
8. Should be able to generate a report via excel/presentation/pdf

The Senior Manager will have these functionalities:

1. Will login using the username (name.surname) and password.
2. They should see dashboard of all the scrap that has been approved / rejected with the TeamLeader, GroupLeader, Manager who approved or rejected.
3. They should be able to approve/reject with a reason. When rejecting, that's only when you add a reason.
4. They should be able to see Managers who have requested to be added onto the system and approve/reject. If new Managers is approved, then gives them credentials.
5. Should be able to generate a report via excel/presentation/pdf

The admin will have these functionalities:

1. Will login using the username (name.surname) and password. 
2. They should see dashboard of all the scrap that has been approved / rejected with approvers.
3. If scrap is approved by all approvers, then they should weigh the scrap and load Kgs. If scrap is not approved, check department, line, and send notification tothe specific approver located to that specific line.
4. They should be able to add/delete/update current/new users.
5. If user is Operator/Teamleader, they should be registered with the clock number provided and tempo password.
6. If user is GroupLeader, Manager, Senior Manager, they should be sent credentials to their company email addresses. ie, name.surname@hesto.co.za with their passwords. 
7. The user is given credentials once they have been approved.

The kitter will have these functionalities:

1. They log in using clock number and password
2. Their job is to load scrap weigh per line location they belong to.
3. only 1 kitter per line should weigh scrap and add kgs onto the scrap.
4. Scrap per line is weigh once all approvers have approved.
5. Their profile will be managed by TeamLeader as per location 


DATABASE


-- Scrap_Collection_Errors linked to ScrapSection
SELECT TOP (1000) [ReasonId]
      ,[ScrapSectionId]
      ,[ScrapReasonName]
      ,[Part_Position]
      ,[Defect_Item]
      ,[Part_Description]
      ,[IsActive]
      ,[CreatedAt]
      ,[ModifiedAt]
  FROM [SysproCompanyB].[dbo].[scpReason]
  --ScrapSection
  SELECT TOP (1000) [ScrapSectionId]
      ,[ScrapSectionName]
      ,[IsActive]
      ,[CreatedAt]
      ,[ModifiedAt]
  FROM [SysproCompanyB].[dbo].[scpScrapSection]


  -- ScrapData
  SELECT TOP (1000) [ScrapId]
      ,[ClockNumber]
      ,[PartNumber]
      ,[Quantity]
      ,[TerminalQuantity]
      ,[ScrapReason]
      ,[WeightKg]
      ,[Length]
      ,[SubmissionDate]
      ,[Status]
      ,[ApprovedBy]
      ,[ApprovalDate]
      ,[ApprovalReason]
      ,[PhysicalWeight]
      ,[Department]
      ,[Line]
      ,[Shift]
      ,[ScrapSectionId]
      ,[ReasonId]
      ,[Position]
      ,[ModifiedAt]
      ,[ModifiedBy]
      ,[CreatedAt]
      ,[CreatedBy]
  FROM [SysproCompanyB].[dbo].[scpScrapData]

  SELECT TOP (1000) [RequestId]
      ,[ClockNumber]
      ,[CellNumber]
      ,[DepartmentId]
      ,[LineId]
      ,[Shift]
      ,[UserType]
      ,[Status]
      ,[RequestDate]
      ,[ProcessedBy]
      ,[ProcessedDate]
      ,[Comments]
      ,[Name]
      ,[Surname]
      ,[Email]
      ,[PhoneNumber]
      ,[TeamLeaderApproval]
      ,[TeamLeaderApprovalDate]
      ,[TeamLeaderApprovalReason]
      ,[GroupLeaderApproval]
      ,[GroupLeaderApprovalDate]
      ,[GroupLeaderApprovalReason]
      ,[ManagerApproval]
      ,[ManagerApprovalDate]
      ,[ManagerApprovalReason]
      ,[SeniorManagerApproval]
      ,[SeniorManagerApprovalDate]
      ,[SeniorManagerApprovalReason]
  FROM [SysproCompanyB].[dbo].[scpRequest]

  SELECT TOP (1000) [RequestId]
      ,[ClockNumber]
      ,[CellNumber]
      ,[DepartmentId]
      ,[LineId]
      ,[Shift]
      ,[UserType]
      ,[Status]
      ,[RequestDate]
      ,[ProcessedBy]
      ,[ProcessedDate]
      ,[Comments]
      ,[Name]
      ,[Surname]
      ,[Email]
      ,[PhoneNumber]
      ,[TeamLeaderApproval]
      ,[TeamLeaderApprovalDate]
      ,[TeamLeaderApprovalReason]
      ,[GroupLeaderApproval]
      ,[GroupLeaderApprovalDate]
      ,[GroupLeaderApprovalReason]
      ,[ManagerApproval]
      ,[ManagerApprovalDate]
      ,[ManagerApprovalReason]
      ,[SeniorManagerApproval]
      ,[SeniorManagerApprovalDate]
      ,[SeniorManagerApprovalReason]
  FROM [SysproCompanyB].[dbo].[scpRequest]

  SELECT TOP (1000) [UserId]
      ,[ClockNumber]
      ,[Password]
      ,[UserType]
      ,[IsTemporary]
      ,[LastPasswordChange]
      ,[IsActive]
      ,[CreatedAt]
      ,[CreatedBy]
      ,[LastModifiedAt]
      ,[LastModifiedBy]
      ,[Department]
      ,[Line]
      ,[Shift]
      ,[Email]
      ,[PhoneNumber]
      ,[PasswordResetRequired]
  FROM [SysproCompanyB].[dbo].[scpUser]

  SELECT TOP (1000) [NotificationId]
      ,[RecipientId]
      ,[NotificationType]
      ,[Message]
      ,[IsRead]
      ,[CreatedAt]
  FROM [SysproCompanyB].[dbo].[scpNotificationLog]

  --Line is linked to Department
  SELECT TOP (1000) [LineId]
      ,[DepartmentId]
      ,[LineName]
      ,[IsActive]
      ,[CreatedAt]
      ,[ModifiedAt]
  FROM [SysproCompanyB].[dbo].[scpdbo].[Line]
  SELECT TOP (1000) [DepartmentId]
      ,[DepartmentName]
      ,[IsActive]
      ,[CreatedAt]
      ,[ModifiedAt]
      ,[Location]
  FROM [SysproCompanyB].[dbo].[scpDepartment]


  SELECT TOP (1000) [ErrorId]
      ,[UserId]
      ,[ClockNumber]
      ,[ErrorMessage]
      ,[StackTrace]
      ,[ErrorSource]
      ,[IPAddress]
      ,[UserAgent]
      ,[CreatedAt]
  FROM [SysproCompanyB].[dbo].[scpErrorLogs]

  SELECT TOP (1000) [HierarchyId]
      ,[Department]
      ,[Line]
      ,[TeamLeader]
      ,[GroupLeader]
      ,[Manager]
      ,[SeniorManager]
  FROM [SysproCompanyB].[dbo].[scpApproverHierarchy]

  SELECT TOP (1000) [ApprovalId]
      ,[ScrapId]
      ,[ApproverClockNumber]
      ,[ApprovalLevel]
      ,[Status]
      ,[Reason]
      ,[ApprovalDate]
  FROM [SysproCompanyB].[dbo].[scpApprovalFlow]

  SELECT TOP (1000) [LogId]
      ,[UserId]
      ,[ClockNumber]
      ,[ActionType]
      ,[ActionDetails]
      ,[Status]
      ,[IPAddress]
      ,[UserAgent]
      ,[SecurityStamp]
      ,[CreatedAt]
      ,[Department]
      ,[Line]
      ,[Shift]
      ,[Module]
      ,[OldValue]
      ,[NewValue]
      ,[AffectedUser]
      ,[DeviceInfo]
      ,[ErrorMessage]
  FROM [SysproCompanyB].[dbo].[scpActivityLogs]

  SELECT TOP (1000) [MigrationId]
      ,[ProductVersion]
  FROM [SysproCompanyB].[dbo].[scp__EFMigrationsHistory]
