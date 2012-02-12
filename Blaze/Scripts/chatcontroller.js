﻿function ChatController(campfire, contentProcessor, view, loginView) {
    this.userCache = {};
    this.contentProcessor = contentProcessor;
    this.campfire = campfire;
    this.view = view;
    this.loginView = loginView;
    this.currentUser = null;
}

ChatController.prototype.init = function (accountName) {
    var self = this,
        account = accountName ? accountName : $.cookie('account'),
        authToken;

    self.view.init(new RoomsModel(this));
    if (account) {
        authToken = $.cookie(account + '_authtoken');
    }
    if (!authToken || !account) {
        self.loginView.show(account, $.proxy(self.login, self));
    } else {
        self.campfire.authToken = authToken;
        self.campfire.setAccount(account);
        self.campfire.login(account, self.campfire.authToken, 'x', function (user) {
            self.showLobby(user);
        });
    }
};

ChatController.prototype.login = function (account, username, password) {
    var self = this;
    self.campfire.login(account, username, password, function (user) {
        $.cookie('account', account, { expires: 1 });
        $.cookie(account + '_authtoken', user.api_auth_token, { expires: 1 });
        self.loginView.hide();
        self.showLobby(user);
    });
};

ChatController.prototype.showLobby = function (user) {
    var self = this;
    var currentUserModel = new UserModel(user);
    self.currentUser = currentUserModel;
    self.userCache[currentUserModel.id()] = currentUserModel;
    self.view.show();
    self.campfire.getRooms(function (rooms) {
        $.map(rooms, function (o) {
            var roomModel = new RoomModel(o, currentUserModel, self);
            self.view.addRoom(roomModel);
            self.loadUsers(roomModel);
        });
    });
};

ChatController.prototype.loadUsers = function (room) {
    var self = this;
    self.campfire.getUsers(room.id(), function (users) {
        room.users.removeAll();
        $.map(users, function (o) {
            var userModel = new UserModel(o);
            room.users.push(userModel);
            self.userCache[userModel.id()] = userModel;
        });
        self.view.sortRooms();
        setTimeout(function () {
            self.loadUsers(room);
        }, 30000);
    });
};

ChatController.prototype.showRoom = function (room, isNewRoom) {
    var self = this;
    if (isNewRoom) {
        self.campfire.enterRoom(room.id(), function () {
            self.loadMessages(room, true);
        });
    }
    self.view.showRoom(room);
};

ChatController.prototype.loadMessages = function (room, autorefresh) {
    var self = this;
    var lastMsgId = room.lastMessage ? room.lastMessage.id() : undefined;
    self.campfire.getRecentMessages(room.id(), lastMsgId, function (messages) {
        var hasContent = false;
        $.map(messages, function (o) {
            var user = o.user_id ? self.getUser(o.user_id) : new UserModel({ id: 0, name: '' });
            var isSeparator = false;
            if (o.type === 'TimestampMessage' && room.lastMessage) {
                var oldDate = new Date(room.lastMessage.created_at()).toDate();
                var newDate = new Date(o.created_at).toDate();
                if (oldDate.diffDays(newDate)) 
                    isSeparator = true;
            }
            if (o.type !== 'TimestampMessage' || isSeparator) {
                var messageModel = new MessageModel(o, user, room.lastMessage, self.contentProcessor);
                room.addMessage(messageModel);
                room.lastMessage = messageModel;
                hasContent = true;
                if (messageModel.type() === 'UploadMessage') {
                    self.campfire.getUploadedMessage(room.id(), o.id, function (up) {
                        var url = self.campfire.getAuthorisedUrl(up.full_url);
                        var body = '<a href="' + url + '" target="_blank" rel="nofollow external" class="file-upload">' + up.name + '</a>';
                        if (up.content_type === 'image/jpeg' || up.content_type === 'image/jpg' || up.content_type === 'image/png' || up.content_type === 'image/gif' || up.content_type === 'image/bmp') {
                            body = '<div class="collapsible_content"><h3 class="collapsible_title">' + up.name + ' (click to show/hide)</h3><div class="collapsible_box"><img src="' + url + '" class="uploadImage"/></div></div>';
                        }
                        messageModel.parsed_body(body);
                    });
                }
            }
        });
        if (hasContent) {
            if (room.isVisible())
                self.view.scrollToEnd();
        }
        if (autorefresh === true) {
            if (room.timer) {
                clearTimeout(room.timer);
            }
            room.timer = setTimeout(function () {
                self.loadMessages(room, true);
            }, room.refreshRate());
        }
    });
};

ChatController.prototype.getUser = function(id) {
    var self = this;
    if (self.userCache[id] !== undefined) {
        return (self.userCache[id]);
    } else {
        var model = new UserModel({ id: id, name: '' });
        self.userCache[id] = model;
        self.campfire.getUser(id, function(user) {
            model.name(user.name);
        });
        return model;
    }
};

ChatController.prototype.sendMessage = function(room, message, isPaste) {
    var self = this;
    self.campfire.sendMessage(room.id(), message, isPaste, function() {
        self.loadMessages(room);
    });
};

ChatController.prototype.leaveRoom = function (room) {
    var self = this;
    self.campfire.leaveRoom(room.id(), function () {
    });
};

